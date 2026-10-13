-- Follow-up to 0001 from Supabase advisors (security + performance).

-- 1) citext out of the public (API-exposed) schema.
alter extension citext set schema extensions;

-- 2) RLS helper functions: move to a non-exposed schema so they are not callable via /rest/v1/rpc.
create schema if not exists private;
revoke all on schema private from public;
grant usage on schema private to authenticated;

alter function public.is_match_member(uuid) set schema private;
alter function public.my_seat(uuid) set schema private;
alter function private.is_match_member(uuid) set search_path = public;
alter function private.my_seat(uuid) set search_path = public;
revoke execute on function private.is_match_member(uuid) from public, anon;
revoke execute on function private.my_seat(uuid) from public, anon;
grant execute on function private.is_match_member(uuid) to authenticated;
grant execute on function private.my_seat(uuid) to authenticated;
-- (policies reference the functions by OID, so existing policies keep working after the move)

-- 3) Username check: users sign in anonymously first, so no anon access is needed, and
--    profiles are readable by authenticated users, so SECURITY INVOKER is enough.
alter function public.is_username_available(text) security invoker;
alter function public.is_username_available(text) set search_path = public, extensions;
revoke execute on function public.is_username_available(text) from public, anon;
grant execute on function public.is_username_available(text) to authenticated;

-- 4) Index the matches.created_by foreign key.
create index if not exists matches_created_by_idx on public.matches (created_by);

-- match_secrets intentionally has RLS enabled and no policies (server/service-role only).
comment on table public.match_secrets is 'Server-only: seed + authoritative state. RLS on, no policies by design.';
