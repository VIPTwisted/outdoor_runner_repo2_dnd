-- enterprise_monitoring_schema.sql
-- Apply once in Supabase SQL Editor. Idempotent.

create schema if not exists dooh;
create extension if not exists pgcrypto;

-- ─── sync_runs ─────────────────────────────────────────────────────
-- One row per GitHub Actions run (or manual invocation). Joined to many
-- sync_events. Joined optionally to sync_requests via request_id.
create table if not exists dooh.sync_runs (
  id              uuid primary key default gen_random_uuid(),
  request_id      uuid references dooh.sync_requests(id) on delete set null,
  mode            text not null check (mode in ('incremental','full','heartbeat')),
  runner          text,                           -- 'github-actions', 'local', etc.
  workflow_run_id text,                           -- GitHub Actions run id
  started_at      timestamptz not null default now(),
  finished_at     timestamptz,
  duration_ms     int,
  status          text not null default 'running' check (status in ('running','succeeded','failed','timeout')),
  error_message   text,
  rows_upserted   int default 0,
  manifest        jsonb                           -- per-section counts, see SyncProcessor
);
create index if not exists idx_sync_runs_started_at on dooh.sync_runs (started_at desc);
create index if not exists idx_sync_runs_status     on dooh.sync_runs (status, started_at desc);

-- ─── sync_events ───────────────────────────────────────────────────
-- Fine-grained forensic log. Each navigation, scrape, upsert, error
-- writes one row. Joined to sync_runs.
create table if not exists dooh.sync_events (
  id             uuid primary key default gen_random_uuid(),
  run_id         uuid references dooh.sync_runs(id) on delete cascade,
  ts             timestamptz not null default now(),
  level          text not null default 'info' check (level in ('debug','info','warn','error')),
  phase          text,                            -- 'login','navigate','scrape','upsert','finalize'
  component      text,                            -- 'boards','campaigns','ads','dashboard', ...
  message        text,
  payload        jsonb
);
create index if not exists idx_sync_events_run_ts   on dooh.sync_events (run_id, ts);
create index if not exists idx_sync_events_level    on dooh.sync_events (level, ts desc);

-- ─── board_status ──────────────────────────────────────────────────
-- Per-board health snapshot. Updated by every sync run that touches a board.
create table if not exists dooh.board_status (
  sign_id                 text primary key,
  state                   text not null default 'unknown' check (state in ('ok','degraded','down','unknown')),
  last_seen_at            timestamptz,
  last_successful_sync_at timestamptz,
  player_id               text,
  region                  text,
  latency_ms              int,
  error_text              text,
  updated_at              timestamptz not null default now()
);
create index if not exists idx_board_status_state on dooh.board_status (state, updated_at desc);
create index if not exists idx_board_status_region on dooh.board_status (region);

-- ─── player_heartbeat ──────────────────────────────────────────────
-- Each Android/Windows player pings us with a tiny POST. Latest row per
-- player wins via (player_id, captured_at) — index makes "latest per player"
-- cheap.
create table if not exists dooh.player_heartbeat (
  id              uuid primary key default gen_random_uuid(),
  player_id       text not null,
  sign_id         text references dooh.blip_signs(sign_id) on delete set null,
  ip_address      inet,
  firmware        text,
  state           text default 'ok' check (state in ('ok','degraded','down','unknown')),
  last_ad_id      text,
  cpu_pct         numeric,
  ram_pct         numeric,
  uptime_seconds  bigint,
  captured_at     timestamptz not null default now(),
  raw             jsonb
);
create index if not exists idx_player_heartbeat_player_ts on dooh.player_heartbeat (player_id, captured_at desc);
create index if not exists idx_player_heartbeat_state     on dooh.player_heartbeat (state, captured_at desc);

create or replace view dooh.player_health_latest as
select distinct on (player_id) *
from dooh.player_heartbeat
order by player_id, captured_at desc;

-- ─── platform_incidents ────────────────────────────────────────────
-- Anything an alert function flags lands here. UI shows open ones by severity.
create table if not exists dooh.platform_incidents (
  id           uuid primary key default gen_random_uuid(),
  kind         text not null,                    -- 'sync.failed','sync.missing','board.down','player.down','netlify.error','supabase.error','runner.timeout'
  severity     text not null default 'warning' check (severity in ('info','warning','critical')),
  subject_id   text,                              -- sign_id / player_id / sync_run_id etc.
  summary      text not null,
  detail       jsonb,
  opened_at    timestamptz not null default now(),
  resolved_at  timestamptz,
  status       text not null default 'open' check (status in ('open','acknowledged','resolved'))
);
create index if not exists idx_platform_incidents_status on dooh.platform_incidents (status, opened_at desc);
create index if not exists idx_platform_incidents_kind   on dooh.platform_incidents (kind, opened_at desc);

notify pgrst, 'reload schema';
