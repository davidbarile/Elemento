-- Presence is advisory. It never changes match state, revision, or updated_at.
alter table public.match_players add column last_seen timestamptz;

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
              and target_match.protocol_version = 2
       );
    if not found then
        return null;
    end if;

    select least(2147483647, greatest(0, floor(extract(epoch from (v_now - opponent.last_seen)))))::integer
      into v_seconds
      from public.match_players as opponent
     where opponent.match_id = p_match_id
       and opponent.user_id <> p_user_id
       and opponent.last_seen is not null;
    return coalesce(v_seconds, -1);
end;
$$;

revoke all on function public.touch_match_presence(uuid, uuid) from public, anon, authenticated;
grant execute on function public.touch_match_presence(uuid, uuid) to service_role;
