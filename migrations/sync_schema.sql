-- outdoor_runner_repo2_dnd / migrations/sync_schema.sql
-- Apply once in Supabase SQL Editor.

create schema if not exists dooh;
create extension if not exists pgcrypto;

create table if not exists dooh.sync_requests (
  id            uuid primary key default gen_random_uuid(),
  mode          text not null check (mode in ('incremental','full')),
  status        text not null default 'pending' check (status in ('pending','processing','completed','failed')),
  created_at    timestamptz not null default now(),
  started_at    timestamptz,
  completed_at  timestamptz,
  error_message text
);
create index if not exists idx_sync_requests_status_created
  on dooh.sync_requests (status, created_at);

create table if not exists dooh.blip_boards (
  id            text primary key,
  name          text,
  location      text,
  status        text,
  last_seen_at  timestamptz,
  raw           jsonb,
  captured_at   timestamptz not null default now()
);

create table if not exists dooh.blip_campaigns (
  id            text primary key,
  name          text,
  advertiser    text,
  status        text,
  start_date    date,
  end_date      date,
  budget        numeric,
  raw           jsonb,
  captured_at   timestamptz not null default now()
);

create table if not exists dooh.blip_ads (
  id              text primary key,
  campaign_id     text references dooh.blip_campaigns(id) on delete set null,
  board_id        text references dooh.blip_boards(id) on delete set null,
  status          text,
  last_served_at  timestamptz,
  raw             jsonb,
  captured_at     timestamptz not null default now()
);

notify pgrst, 'reload schema';
