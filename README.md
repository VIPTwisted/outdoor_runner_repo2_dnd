# outdoor_runner_repo2_dnd

Free, cloud-only BLIP to Supabase sync runner for the DeMartino Outdoor Media platform.

- Runs from GitHub Actions on Windows.
- Logs into BLIP through Selenium Edge using encrypted GitHub Actions secrets.
- Reads pending sync requests from `dooh.sync_requests`.
- Scrapes the authenticated BLIP operator UI only.
- Writes raw captures into the existing `dooh.scraped_*` tables.
- Upserts normalized rows into the existing `dooh.blip_*` / canonical DOOH tables where the schema has a stable key.
- Is triggered by the Netlify platform inserting `dooh.sync_requests` rows.
- Uses no BLIP API keys, no bearer tokens, no BLIP webhooks, no desktop daemon, and no paid runner.

## Architecture

```text
Platform button/API -> dooh.sync_requests row
                    -> GitHub Actions Windows runner
                    -> Selenium Edge authenticated BLIP UI session
                    -> Supabase dooh.* mirror tables
                    -> Netlify platform reads Supabase
```

The platform never calls BLIP from the browser. BLIP credentials only exist as encrypted GitHub Actions secrets and are only used inside the runner VM.

## Required GitHub Actions configuration

Repository: `VIPTwisted/outdoor_runner_repo2_dnd`

Required repository secrets:

| Name | Purpose |
| --- | --- |
| `BLIP_USERNAME` | BLIP login email for the sync account |
| `BLIP_PASSWORD` | BLIP login password for the sync account |
| `POSTGRES_CONNECTION_STRING` | Supabase transaction-pooler connection string in Npgsql key/value format |

Required repository variables:

| Name | Default |
| --- | --- |
| `BLIP_BASE_URL` | `https://operator.blipbillboards.com` |
| `BLIP_OPERATOR_SLUG` | `k7b6gz` |

The confirmed Supabase connection target is:

```text
Host=aws-1-us-east-1.pooler.supabase.com;Port=6543;Database=postgres;Username=postgres.ctmtpjdrsgtnwgwsxmsa;SSL Mode=Require;Trust Server Certificate=true
```

Do not place the database password in this file.

## Captured BLIP sections

The runner follows the documented BLIP/Adkom mirror surface and writes to the live schema already present in Supabase.

| BLIP section | Route | Raw table | Normalized table |
| --- | --- | --- | --- |
| Dashboard widgets | `/{slug}/dashboard` | `dooh.scraped_dashboard_widgets` | run manifest only |
| Plant signs | `/{slug}/signs/signs` | `dooh.scraped_plant_signs` | `dooh.blip_plant_signs` |
| Adkom availability | `/{slug}/adkom/availability` | `dooh.scraped_adkom_avails` | `dooh.blip_avails` |
| Adkom holds | `/{slug}/adkom/hold` | `dooh.scraped_adkom_holds` | `dooh.blip_holds` |
| Adkom contracts | `/{slug}/adkom/contract` | `dooh.scraped_adkom_contracts` | `dooh.blip_contracts` |
| Adkom creatives | `/{slug}/adkom/creative` | `dooh.scraped_adkom_creatives` | raw queue only |
| Adkom POP | `/{slug}/adkom/pop` | `dooh.scraped_adkom_pop` | `dooh.blip_pop_reports` |
| Marketplace | `/{slug}/marketplace` | `dooh.scraped_marketplace_groups` | raw queue only |
| Programmatic reports | `/{slug}/programmatic/report` | `dooh.scraped_programmatic_reports` | raw queue only |

## False-success guard

If login completes but every documented scrape target returns zero rows, the runner fails the sync run and opens a platform incident. This prevents a GitHub Actions "green" run from being treated as a working BLIP mirror when selectors or BLIP routes have drifted.

## Validation

Build locally:

```powershell
dotnet build outdoor_runner_repo2_dnd.sln --configuration Release --no-restore
```

Check the latest run in Supabase:

```sql
select id, status, started_at, finished_at, source_payload
from dooh.sync_runs
order by started_at desc
limit 5;

select ts, level, component, message, context
from dooh.sync_events
where sync_run_id = '<latest-run-id>'
order by ts;
```

Expected proof of a real sync is non-zero row counts in `source_payload.manifest` and `persist` events for the captured sections.
