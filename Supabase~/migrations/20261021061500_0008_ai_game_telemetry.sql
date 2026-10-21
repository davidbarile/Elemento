-- Best-effort VS AI game telemetry. Clients write only through Edge Functions.

create table public.ai_games (
    id             uuid primary key default gen_random_uuid(),
    owner_id       uuid not null references auth.users (id) on delete cascade,
    client_game_id text not null check (client_game_id <> ''),
    schema_version smallint not null,
    status         text not null check (status in (
        'completed', 'interrupted', 'quit', 'abandoned', 'restarted', 'corrupt'
    )),
    app_version    text,
    ai_version     text,
    started_at     timestamptz,
    ended_at       timestamptz,
    payload        jsonb not null,
    created_at     timestamptz not null default now(),
    updated_at     timestamptz not null default now(),
    unique (owner_id, client_game_id)
);

create index ai_games_owner_created_idx on public.ai_games (owner_id, created_at desc);
create index ai_games_status_created_idx on public.ai_games (status, created_at desc);

alter table public.ai_games enable row level security;
revoke all on table public.ai_games from public, anon, authenticated;
grant select, insert, update, delete on table public.ai_games to service_role;
