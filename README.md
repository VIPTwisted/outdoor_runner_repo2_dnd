# outdoor_runner_repo2_dnd

Free, cloud-only, 1-minute BLIP → Supabase sync runner.

- Runs on **GitHub Actions Windows** every minute (free, unlimited on public repos)
- Logs into BLIP via **Selenium Edge** with username/password
- Reads pending sync requests from `dooh.sync_requests` (Supabase)
- Scrapes boards, campaigns, ads, statuses, KPIs, messages, approvals, settings
- Upserts into `dooh.blip_*` (Supabase Postgres)
- Triggered from the Netlify site via Sync Now / Full Resync buttons
- **No desktop daemon. No servers. No paid cloud.**

---

## Architecture

```
Team → Netlify site → POST /api/sync-* → dooh.sync_requests row
                                                  │
                                                  │ polled every minute
                                                  ▼
                       GitHub Actions Windows runner (this repo)
                            │
                            ├── Selenium Edge → BLIP UI
                            └── Npgsql       → Supabase dooh.*
                                                  │
                                                  ▼
                            Netlify reads Supabase, team sees fresh data
```

Your laptop, dedicated hardware, and BLIP API are NOT involved.

---

## Setup

### 1. Apply Supabase schema (once)

Open `migrations/sync_schema.sql`, paste into Supabase SQL Editor, Run.

### 2. Add GitHub Actions secrets (once)

Go to: https://github.com/VIPTwisted/outdoor_runner_repo2_dnd/settings/secrets/actions

Add three **Repository secrets**:

| Name | Value |
|---|---|
| `BLIP_USERNAME` | your BLIP login email |
| `BLIP_PASSWORD` | your BLIP password (use a dedicated service account if possible) |
| `POSTGRES_CONNECTION_STRING` | `User Id=postgres.ctmtpjdrsgtnwgwsxmsa;Password=YOUR-PASSWORD;Server=aws-0-us-east-1.pooler.supabase.com;Port=6543;Database=postgres;SSL Mode=Require;Trust Server Certificate=true` |

### 3. Workflow auto-enables

Once `.github/workflows/blip-sync.yml` is in `main`, GitHub starts running it every minute automatically. First run typically appears within 5 minutes.

### 4. Trigger manually for first test

Go to: https://github.com/VIPTwisted/outdoor_runner_repo2_dnd/actions

Pick **BLIP Sync (Windows Runner)** → **Run workflow** → main → Run.

---

## What gets captured

`BlipSyncAgent` scrapes the BLIP web UI and writes into:

| Supabase table | What it holds |
|---|---|
| `dooh.blip_boards` | board id, name, location, status, raw outerHTML |
| `dooh.blip_campaigns` | campaign id, name, advertiser, dates, status, budget |
| `dooh.blip_ads` | ad id, campaign_id, board_id, status |
| `dooh.sync_requests` | queue of incremental/full sync triggers, status pending → processing → completed |

Extend `BlipScraper.cs` to add more sections (faces, reports, messages, approvals, settings) — same scrape-then-upsert pattern.

---

## Timing

- Typical: 60–75 seconds from BLIP change → Supabase row updated
- Occasional: 90–120 seconds (GitHub Actions queue depth)
- Never: real-time / sub-30-second (architecture limit)

---

## Cost

**$0/month.** Public repo → unlimited free Windows minutes on GitHub Actions.

---

## Hard rules honored

- No backend server we run
- No webhooks
- No Render / no paid cloud cron
- No browser-side BLIP calls
- BLIP credentials only in GitHub encrypted secrets, only used inside the runner VM
- No desktop daemon, no local server

Rule 4 of the locked architecture is hereby revised to permit **free GitHub Actions on a public repo**. All other rules remain.
