-- Commit an accepted move and all derived state atomically.

create or replace function private.commit_move(
    p_match uuid,
    p_expected_revision integer,
    p_new_revision integer,
    p_seat smallint,
    p_client_nonce text,
    p_command jsonb,
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
    v_revision integer;
begin
    select match_move.revision
      into v_revision
      from public.match_moves as match_move
     where match_move.match_id = p_match
       and match_move.seat = p_seat
       and match_move.client_nonce = p_client_nonce;

    if found then
        return jsonb_build_object('duplicate', true, 'revision', v_revision);
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
    values (p_match, p_new_revision, p_seat, p_client_nonce, p_command);

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

    return jsonb_build_object('ok', true, 'revision', p_new_revision);
end;
$$;

revoke execute on function private.commit_move(uuid, integer, integer, smallint, text, jsonb, jsonb, jsonb, public.match_status, smallint, integer, smallint)
    from public, anon, authenticated;
grant execute on function private.commit_move(uuid, integer, integer, smallint, text, jsonb, jsonb, jsonb, public.match_status, smallint, integer, smallint)
    to service_role;

create or replace function public.commit_move(
    p_match uuid,
    p_expected_revision integer,
    p_new_revision integer,
    p_seat smallint,
    p_client_nonce text,
    p_command jsonb,
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
    select private.commit_move(
        p_match,
        p_expected_revision,
        p_new_revision,
        p_seat,
        p_client_nonce,
        p_command,
        p_state,
        p_views,
        p_status,
        p_current_seat,
        p_turn_number,
        p_winner
    );
$$;

revoke execute on function public.commit_move(uuid, integer, integer, smallint, text, jsonb, jsonb, jsonb, public.match_status, smallint, integer, smallint)
    from public, anon, authenticated;
grant execute on function public.commit_move(uuid, integer, integer, smallint, text, jsonb, jsonb, jsonb, public.match_status, smallint, integer, smallint)
    to service_role;
