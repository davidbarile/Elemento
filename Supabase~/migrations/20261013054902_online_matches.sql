-- Elemento online multiplayer: DRAFT schema (not applied anywhere).
-- Authority model: clients never write match tables directly. All writes go through the
-- `match-move` / `match-create` / `match-join` Edge Functions using the service role, which
-- validate commands with the authoritative rules engine and bump `revision` transactionally.
-- Hidden information (deck order, seed, opponent hands) lives only in `match_secrets`, which
-- has RLS enabled and NO policies, so anon/authenticated roles can never read it.

create extension if not exists pgcrypto;

create type public.match_status as enum ('waiting', 'active', 'finished', 'abandoned');
create type public.match_mode as enum ('live', 'async');

-- Auth: Supabase anonymous sign-in (auth.users.is_anonymous = true) + a chosen username.
-- Anonymous users can later link Apple (iOS) / Google (Android) / both (desktop/web) via
-- identity linking; the auth.users id stays the same, so profiles and match history carry over.
create extension if not exists citext;

create table public.profiles (
    id           uuid primary key references auth.users (id) on delete cascade,
    username     citext not null unique                      -- case-insensitive unique handle
                 check (username ~ '^[A-Za-z0-9_]{3,20}$'),
    created_at   timestamptz not null default now(),
    updated_at   timestamptz not null default now()
);

-- Cheap availability check for the username picker (does not expose other profile data).
create or replace function public.is_username_available(p_username text)
returns boolean language sql stable security definer set search_path = public as $$
    select p_username ~ '^[A-Za-z0-9_]{3,20}$'
       and not exists (select 1 from public.profiles where username = p_username::citext);
$$;
grant execute on function public.is_username_available(text) to anon, authenticated;

create table public.matches (
    id               uuid primary key default gen_random_uuid(),
    status           public.match_status not null default 'waiting',
    mode             public.match_mode   not null default 'async',
    protocol_version smallint not null default 1,
    rules            jsonb    not null,                 -- RulesConfigData (public)
    invite_code      text unique,                       -- short code for friend invites
    revision         integer  not null default 0,       -- bumps on every accepted command
    turn_number      integer  not null default 0,
    current_seat     smallint,
    winner_seat      smallint,
    turn_deadline    timestamptz,                       -- async: forfeit/nudge after this
    created_by       uuid not null references auth.users (id),
    created_at       timestamptz not null default now(),
    updated_at       timestamptz not null default now()
);

create table public.match_players (
    match_id     uuid     not null references public.matches (id) on delete cascade,
    seat         smallint not null check (seat in (0, 1)),
    user_id      uuid     not null references auth.users (id),
    deck         jsonb    not null,                     -- DeckDefinitionData chosen at join (card definitions, not order)
    joined_at    timestamptz not null default now(),
    last_seen_at timestamptz not null default now(),
    primary key (match_id, seat),
    unique (match_id, user_id)
);

-- Server-only: seed, authoritative full GameStateData, deck order. Never exposed.
create table public.match_secrets (
    match_id   uuid primary key references public.matches (id) on delete cascade,
    seed       bigint not null,
    state      jsonb  not null,                         -- full authoritative state at matches.revision
    updated_at timestamptz not null default now()
);

-- Accepted command log (public actions only; draws are server-side effects, not commands).
create table public.match_moves (
    match_id     uuid     not null references public.matches (id) on delete cascade,
    revision     integer  not null,                     -- revision AFTER applying this command
    seat         smallint not null,
    client_nonce text     not null,                     -- idempotency key from MoveCommandData
    command      jsonb    not null,                     -- MoveCommandData
    created_at   timestamptz not null default now(),
    primary key (match_id, revision),
    unique (match_id, seat, client_nonce)
);

-- Per-seat fair view (MatchSeatViewData): own hand + public boards + counts only.
create table public.match_seat_views (
    match_id   uuid     not null references public.matches (id) on delete cascade,
    seat       smallint not null,
    revision   integer  not null,
    view       jsonb    not null,
    updated_at timestamptz not null default now(),
    primary key (match_id, seat)
);

create index match_players_user_idx on public.match_players (user_id);
create index matches_status_idx on public.matches (status, updated_at);

-- Helpers -------------------------------------------------------------------
create or replace function public.is_match_member(p_match uuid)
returns boolean language sql stable security definer set search_path = public as $$
    select exists (select 1 from public.match_players where match_id = p_match and user_id = auth.uid());
$$;

create or replace function public.my_seat(p_match uuid)
returns smallint language sql stable security definer set search_path = public as $$
    select seat from public.match_players where match_id = p_match and user_id = auth.uid();
$$;

-- RLS -----------------------------------------------------------------------
alter table public.profiles         enable row level security;
alter table public.matches          enable row level security;
alter table public.match_players    enable row level security;
alter table public.match_secrets    enable row level security;   -- no policies on purpose
alter table public.match_moves      enable row level security;
alter table public.match_seat_views enable row level security;

-- Anonymous users are role "authenticated" (with is_anonymous claim), so these cover them too.
create policy profiles_select on public.profiles for select to authenticated using (true);
create policy profiles_insert_own on public.profiles for insert to authenticated
    with check (id = (select auth.uid()));
create policy profiles_update_own on public.profiles for update to authenticated
    using (id = (select auth.uid())) with check (id = (select auth.uid()));
-- Only username/updated_at are client-editable.
revoke update on public.profiles from authenticated;
grant update (username, updated_at) on public.profiles to authenticated;

-- Members see their matches; waiting matches are discoverable only via invite code in the join function.
create policy matches_select_member on public.matches for select to authenticated
    using (public.is_match_member(id));

create policy match_players_select_member on public.match_players for select to authenticated
    using (public.is_match_member(match_id));

create policy match_moves_select_member on public.match_moves for select to authenticated
    using (public.is_match_member(match_id));

-- Each player can read ONLY their own seat's view.
create policy match_seat_views_select_own on public.match_seat_views for select to authenticated
    using (seat = public.my_seat(match_id));

-- No insert/update/delete policies for clients on match tables: Edge Functions use the service role.

-- Realtime: private broadcast topic "match:<id>" carrying only {revision}. Clients then refetch
-- their own seat view. Restrict channel joins to members.
create policy realtime_match_members on realtime.messages for select to authenticated
    using (
        realtime.topic() like 'match:%'
        and public.is_match_member(substring(realtime.topic() from 7)::uuid)
    );

-- Optimistic concurrency used by the move Edge Function inside one transaction:
--   update public.matches set revision = revision + 1, updated_at = now(), ...
--    where id = $match and revision = $expected_revision and status = 'active'
--   returning revision;   -- 0 rows => stale revision, reject command
