-- Commit a fully validated turn and all derived state atomically.

alter table public.match_moves alter column client_nonce drop not null;

-- Move logs are served through get-view so opponent draw identities can be redacted.
revoke select on public.match_moves from anon, authenticated;
drop policy if exists match_moves_select_member on public.match_moves;

create or replace function private.commit_turn(
    p_match uuid,
    p_expected_revision integer,
    p_new_revision integer,
    p_seat smallint,
    p_client_nonce text,
    p_commands jsonb,
    p_state jsonb,
    p_views jsonb,
    p_status public.match_status,
    p_current_seat smallint,
    p_turn_number integer,
    p_winner smallint
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
    v_move_count integer;
    v_revision integer;
begin
    select match_move.revision,
           coalesce((match_move.command ->> 'ClientMoveSequence')::integer, 1)
      into v_revision, v_move_count
      from public.match_moves as match_move
     where match_move.match_id = p_match
       and match_move.seat = p_seat
       and match_move.client_nonce = p_client_nonce;

    if found then
        return jsonb_build_object('duplicate', true, 'revision', v_revision, 'move_count', v_move_count);
    end if;

    if p_client_nonce is null or p_client_nonce = '' then
        raise exception 'p_client_nonce is required';
    end if;
    if jsonb_typeof(p_commands) is distinct from 'array' then
        raise exception 'p_commands must be a JSON array';
    end if;
    v_move_count := jsonb_array_length(p_commands);
    if v_move_count = 0 or p_new_revision <> p_expected_revision + v_move_count then
        raise exception 'invalid turn revision range';
    end if;

    update public.matches as target_match
       set revision = p_new_revision,
           status = p_status,
           current_seat = p_current_seat,
           turn_number = p_turn_number,
           winner_seat = p_winner,
           updated_at = now()
     where target_match.id = p_match
       and target_match.revision = p_expected_revision
       and target_match.status = 'active'
    returning target_match.revision into v_revision;

    if not found then
        return jsonb_build_object('stale', true);
    end if;

    insert into public.match_moves (match_id, revision, seat, client_nonce, command)
    select p_match,
           p_expected_revision + command_entry.ordinality::integer,
           p_seat,
           case when command_entry.ordinality = v_move_count then p_client_nonce else null end,
           command_entry.command
      from jsonb_array_elements(p_commands) with ordinality as command_entry(command, ordinality);

    update public.match_secrets
       set state = p_state,
           updated_at = now()
     where match_id = p_match;

    insert into public.match_seat_views (match_id, seat, revision, view, updated_at)
    select p_match,
           (seat_view.entry ->> 'seat')::smallint,
           p_new_revision,
           seat_view.entry -> 'view',
           now()
      from jsonb_array_elements(p_views) as seat_view(entry)
    on conflict (match_id, seat) do update
       set revision = excluded.revision,
           view = excluded.view,
           updated_at = excluded.updated_at;

    return jsonb_build_object('ok', true, 'revision', p_new_revision, 'move_count', v_move_count);
end;
$$;

revoke execute on function private.commit_turn(uuid, integer, integer, smallint, text, jsonb, jsonb, jsonb, public.match_status, smallint, integer, smallint)
    from public, anon, authenticated;
grant execute on function private.commit_turn(uuid, integer, integer, smallint, text, jsonb, jsonb, jsonb, public.match_status, smallint, integer, smallint)
    to service_role;

create or replace function public.commit_turn(
    p_match uuid,
    p_expected_revision integer,
    p_new_revision integer,
    p_seat smallint,
    p_client_nonce text,
    p_commands jsonb,
    p_state jsonb,
    p_views jsonb,
    p_status public.match_status,
    p_current_seat smallint,
    p_turn_number integer,
    p_winner smallint
)
returns jsonb
language sql
security definer
set search_path = public
as $$
    select private.commit_turn(
        p_match,
        p_expected_revision,
        p_new_revision,
        p_seat,
        p_client_nonce,
        p_commands,
        p_state,
        p_views,
        p_status,
        p_current_seat,
        p_turn_number,
        p_winner
    );
$$;

revoke execute on function public.commit_turn(uuid, integer, integer, smallint, text, jsonb, jsonb, jsonb, public.match_status, smallint, integer, smallint)
    from public, anon, authenticated;
grant execute on function public.commit_turn(uuid, integer, integer, smallint, text, jsonb, jsonb, jsonb, public.match_status, smallint, integer, smallint)
    to service_role;
