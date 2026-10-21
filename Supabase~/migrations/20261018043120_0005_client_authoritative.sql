-- Protocol v2: clients author complete turns and full resulting state.

alter table public.matches alter column protocol_version set default 2;
alter table public.match_players alter column deck drop not null;

create table public.match_state (
    match_id   uuid primary key references public.matches (id) on delete cascade,
    state      jsonb not null,
    state_hash text not null,
    updated_at timestamptz not null default now()
);

create table public.match_turns (
    match_id     uuid not null references public.matches (id) on delete cascade,
    revision     integer not null,
    seat         smallint not null check (seat in (0, 1)),
    client_nonce text not null check (client_nonce <> ''),
    package      jsonb not null,
    created_at   timestamptz not null default now(),
    primary key (match_id, revision),
    unique (match_id, client_nonce)
);

alter table public.match_state enable row level security;
alter table public.match_turns enable row level security;
revoke all on table public.match_state from public, anon, authenticated;
revoke all on table public.match_turns from public, anon, authenticated;
grant select, insert, update, delete on table public.match_state to service_role;
grant select, insert, update, delete on table public.match_turns to service_role;

create or replace function public.load_match_state_update(
    p_match_id uuid,
    p_user_id uuid,
    p_since_revision integer
)
returns jsonb
language sql
stable
security definer
set search_path = ''
as $$
    select jsonb_build_object(
        'MatchId', target_match.id,
        'Status', target_match.status,
        'MatchRevision', target_match.revision,
        'CurrentSeat', coalesce(target_match.current_seat, -1),
        'YourSeat', caller.seat,
        'WinnerSeat', coalesce(target_match.winner_seat, -1),
        'Rules', target_match.rules,
        'State', stored_state.state,
        'Turns', coalesce((
            select jsonb_agg(stored_turn.package order by stored_turn.revision)
              from public.match_turns as stored_turn
             where stored_turn.match_id = target_match.id
               and stored_turn.revision > p_since_revision
        ), '[]'::jsonb),
        'SeatNames', jsonb_build_array(
            coalesce((
                select profile.username::text
                  from public.match_players as player
                  join public.profiles as profile on profile.id = player.user_id
                 where player.match_id = target_match.id
                   and player.seat = 0
            ), ''),
            coalesce((
                select profile.username::text
                  from public.match_players as player
                  join public.profiles as profile on profile.id = player.user_id
                 where player.match_id = target_match.id
                   and player.seat = 1
            ), '')
        )
    )
      from public.matches as target_match
      join public.match_state as stored_state on stored_state.match_id = target_match.id
      join public.match_players as caller
        on caller.match_id = target_match.id
       and caller.user_id = p_user_id
     where target_match.id = p_match_id
       and target_match.protocol_version = 2;
$$;

revoke all on function public.load_match_state_update(uuid, uuid, integer)
    from public, anon, authenticated;
grant execute on function public.load_match_state_update(uuid, uuid, integer)
    to service_role;

create or replace function public.join_client_match(
    p_match_id uuid,
    p_user_id uuid
)
returns jsonb
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_revision integer;
begin
    select target_match.revision
      into v_revision
      from public.matches as target_match
     where target_match.id = p_match_id
       and target_match.status = 'waiting'
       and target_match.protocol_version = 2
       and target_match.created_by <> p_user_id
     for update;

    if not found
       or exists (
           select 1
             from public.match_players as player
            where player.match_id = p_match_id
              and (player.seat = 1 or player.user_id = p_user_id)
       ) then
        return jsonb_build_object('unavailable', true);
    end if;

    insert into public.match_players (match_id, seat, user_id)
    values (p_match_id, 1, p_user_id);

    update public.matches
       set status = 'active',
           updated_at = now()
     where id = p_match_id;

    return jsonb_build_object('ok', true, 'revision', v_revision);
end;
$$;

revoke all on function public.join_client_match(uuid, uuid)
    from public, anon, authenticated;
grant execute on function public.join_client_match(uuid, uuid)
    to service_role;

revoke all on function public.commit_move(
    uuid, integer, integer, smallint, text, jsonb, jsonb, jsonb,
    public.match_status, smallint, integer, smallint
) from public, anon, authenticated;
revoke all on function private.commit_move(
    uuid, integer, integer, smallint, text, jsonb, jsonb, jsonb,
    public.match_status, smallint, integer, smallint
) from public, anon, authenticated;
revoke all on function public.commit_turn(
    uuid, integer, integer, smallint, text, jsonb, jsonb, jsonb,
    public.match_status, smallint, integer, smallint
) from public, anon, authenticated;
revoke all on function private.commit_turn(
    uuid, integer, integer, smallint, text, jsonb, jsonb, jsonb,
    public.match_status, smallint, integer, smallint
) from public, anon, authenticated;

drop function public.commit_move(
    uuid, integer, integer, smallint, text, jsonb, jsonb, jsonb,
    public.match_status, smallint, integer, smallint
);
drop function private.commit_move(
    uuid, integer, integer, smallint, text, jsonb, jsonb, jsonb,
    public.match_status, smallint, integer, smallint
);
drop function public.commit_turn(
    uuid, integer, integer, smallint, text, jsonb, jsonb, jsonb,
    public.match_status, smallint, integer, smallint
);
drop function private.commit_turn(
    uuid, integer, integer, smallint, text, jsonb, jsonb, jsonb,
    public.match_status, smallint, integer, smallint
);

create or replace function public.commit_client_turn(
    p_match_id uuid,
    p_seat smallint,
    p_expected_revision integer,
    p_nonce text,
    p_package jsonb,
    p_state jsonb,
    p_next_seat smallint,
    p_status public.match_status,
    p_winner_seat smallint
)
returns jsonb
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_revision integer;
begin
    select stored_turn.revision
      into v_revision
      from public.match_turns as stored_turn
     where stored_turn.match_id = p_match_id
       and stored_turn.client_nonce = p_nonce;

    if found then
        return jsonb_build_object('duplicate', true, 'revision', v_revision);
    end if;

    if p_nonce is null or p_nonce = '' then
        raise exception 'p_nonce is required';
    end if;
    if jsonb_typeof(p_package) is distinct from 'object' then
        raise exception 'p_package must be a JSON object';
    end if;
    if jsonb_typeof(p_state) is distinct from 'object' then
        raise exception 'p_state must be a JSON object';
    end if;

    update public.matches as target_match
       set revision = target_match.revision + 1,
           current_seat = p_next_seat,
           status = p_status,
           winner_seat = nullif(p_winner_seat, -1),
           turn_number = coalesce((p_package ->> 'TurnNumber')::integer, target_match.turn_number),
           updated_at = now()
     where target_match.id = p_match_id
       and target_match.revision = p_expected_revision
       and target_match.current_seat = p_seat
       and target_match.status = 'active'
    returning target_match.revision into v_revision;

    if not found then
        select stored_turn.revision
          into v_revision
          from public.match_turns as stored_turn
         where stored_turn.match_id = p_match_id
           and stored_turn.client_nonce = p_nonce;
        if found then
            return jsonb_build_object('duplicate', true, 'revision', v_revision);
        end if;
        return jsonb_build_object('stale', true);
    end if;

    insert into public.match_turns (match_id, revision, seat, client_nonce, package)
    values (p_match_id, v_revision, p_seat, p_nonce, p_package);

    insert into public.match_state (match_id, state, state_hash, updated_at)
    values (p_match_id, p_state, coalesce(p_package ->> 'FinalStateHash', ''), now())
    on conflict (match_id) do update
       set state = excluded.state,
           state_hash = excluded.state_hash,
           updated_at = excluded.updated_at;

    return jsonb_build_object('ok', true, 'revision', v_revision);
end;
$$;

revoke all on function public.commit_client_turn(
    uuid, smallint, integer, text, jsonb, jsonb, smallint, public.match_status, smallint
) from public, anon, authenticated;
grant execute on function public.commit_client_turn(
    uuid, smallint, integer, text, jsonb, jsonb, smallint, public.match_status, smallint
) to service_role;

create or replace function public.abandon_stale_matches()
returns integer
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_count integer;
begin
    update public.matches
       set status = 'abandoned',
           updated_at = now()
     where status in ('waiting', 'active')
       and updated_at < now() - interval '3 days';
    get diagnostics v_count = row_count;
    return v_count;
end;
$$;

revoke all on function public.abandon_stale_matches() from public, anon, authenticated;
grant execute on function public.abandon_stale_matches() to service_role;

do $$
begin
    if exists (select 1 from pg_extension where extname = 'pg_cron') then
        execute $schedule$
            select cron.schedule(
                'elemento-abandon-stale-matches',
                '0 * * * *',
                'select public.abandon_stale_matches()'
            )
             where not exists (
                 select 1
                   from cron.job
                  where jobname = 'elemento-abandon-stale-matches'
             )
        $schedule$;
    end if;
end;
$$;

update public.matches
   set status = 'abandoned',
       updated_at = now()
 where id::text like any(array[
       '8537ff0a%',
       '780c2366%',
       '5c28052b%',
       '8382c4f6%',
       '97e01c50%'
   ])
   and status <> 'finished';
