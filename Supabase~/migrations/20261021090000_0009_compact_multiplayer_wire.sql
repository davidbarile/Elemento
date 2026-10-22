-- Protocol v3 stores multiplayer card definitions once per state and card positions as refs.
-- Existing v2 matches cannot be decoded by v3 clients, so remove only those match rows.

delete from public.matches where protocol_version <> 3;
alter table public.matches alter column protocol_version set default 3;

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
       and target_match.protocol_version = 3;
$$;

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
       and target_match.protocol_version = 3
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

create or replace function public.touch_match_presence(p_match_id uuid, p_user_id uuid)
returns integer
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_now timestamptz := clock_timestamp();
    v_seconds integer;
begin
    update public.match_players as caller
       set last_seen = v_now
     where caller.match_id = p_match_id
       and caller.user_id = p_user_id
       and exists (
           select 1 from public.matches as target_match
            where target_match.id = p_match_id
              and target_match.protocol_version = 3
       );
    if not found then
        return null;
    end if;

    select least(2147483647, greatest(0,
        floor(extract(epoch from (v_now - opponent.last_seen)))))::integer
      into v_seconds
      from public.match_players as opponent
     where opponent.match_id = p_match_id
       and opponent.user_id <> p_user_id
       and opponent.last_seen is not null;
    return coalesce(v_seconds, -1);
end;
$$;
