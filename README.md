# outdoor_runner_repo2_dnd

Free, cloud-only BLIP to Supabase sync runner for the DeMartino Outdoor Media platform.

- Runs from GitHub Actions on Windows.
- Logs into BLIP through Selenium Edge using encrypted GitHub Actions secrets.
- Reads pending sync requests from `dooh.sync_requests`.
- Scrapes the authenticated BLIP operator UI only.
- Writes typed raw captures into the existing `dooh.scraped_*` tables.
- Mirrors visited BLIP pages into generic forensic tables for page text, discovered links, media assets, and network payloads.
- Follows discovered same-tenant detail links so item/detail pages are captured, not just top-level navigation pages.
- Upserts normalized rows into the existing `dooh.blip_*` / canonical DOOH tables where the schema has a stable key.
- Is triggered by the Netlify platform inserting `dooh.sync_requests` rows or by the scheduled heartbeat.
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
| `POSTGRES_CONNECTION_STRING` | Supabase transaction-pooler connection string in Npgsql-compatible format |

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

The runner follows the documented BLIP/Adkom mirror surface and writes to the live schema already present in Supabase. It captures typed row data where selectors are stable and generic page-level forensic data for every visited screen.

| BLIP section | Route | Raw table | Normalized table |
| --- | --- | --- | --- |
| Dashboard widgets | `/{slug}/dashboard` | `dooh.scraped_dashboard_widgets` | run manifest only |
| Plant signs | `/{slug}/signs/signs` | `dooh.scraped_plant_signs` | `dooh.blip_plant_signs` |
| Plant preview | `/{slug}/signs/preview` | `dooh.blip_page_snapshots`, `dooh.blip_network_payloads` | mirror-only pending typed normalizer |
| Plant ads | `/{slug}/signs/ads` | `dooh.blip_page_snapshots`, `dooh.blip_network_payloads` | mirror-only pending typed normalizer |
| Image verification | `/{slug}/signs/image-verification` and discovered `/ads/imageverification` | `dooh.blip_page_snapshots`, `dooh.blip_media_assets`, `dooh.blip_network_payloads` | mirror-only pending typed normalizer |
| Slot assignments / booked space | `/{slug}/signs/assignments` | `dooh.blip_page_snapshots`, `dooh.blip_network_payloads` | mirror-only pending typed normalizer |
| Advertisers | `/{slug}/signs/advertiser` | `dooh.blip_page_snapshots`, `dooh.blip_network_payloads` | mirror-only pending typed normalizer |
| Plant reports | `/{slug}/signs/reports` | `dooh.blip_page_snapshots`, `dooh.blip_network_payloads` | mirror-only pending typed normalizer |
| Campaigns | `/{slug}/campaigns` | `dooh.blip_page_snapshots`, `dooh.blip_network_payloads` | mirror-only pending typed normalizer |
| Ads | `/{slug}/ads` and discovered `/ads/ads` | `dooh.blip_page_snapshots`, `dooh.blip_media_assets`, `dooh.blip_network_payloads` | mirror-only pending typed normalizer |
| Tier 1 ad moderation | discovered `/{slug}/moderation/t1` | `dooh.blip_page_snapshots`, `dooh.blip_network_payloads` | mirror-only pending typed normalizer |
| Tier 2 ad moderation | discovered `/{slug}/moderation/t2` | `dooh.blip_page_snapshots`, `dooh.blip_network_payloads` | mirror-only pending typed normalizer |
| Adkom availability | `/{slug}/adkom/available` | `dooh.scraped_adkom_avails` | `dooh.blip_avails` |
| Adkom holds | `/{slug}/adkom/holds` | `dooh.scraped_adkom_holds` | `dooh.blip_holds` |
| Adkom contracts | `/{slug}/adkom/contracts` | `dooh.scraped_adkom_contracts` | `dooh.blip_contracts` |
| Adkom creatives | `/{slug}/adkom/creatives` | `dooh.scraped_adkom_creatives`, `dooh.blip_media_assets` | raw queue only |
| Adkom POP | `/{slug}/adkom/pops` | `dooh.scraped_adkom_pop` | `dooh.blip_pop_reports` |
| Marketplace | `/{slug}/organizations/marketplace-analytics` | `dooh.scraped_marketplace_groups` | raw queue only |
| Programmatic reports | `/{slug}/organizations/programmatic/reports` | `dooh.scraped_programmatic_reports`, `dooh.blip_network_payloads` | raw queue only |

## Generic BLIP mirror tables

The generic mirror layer exists so the DeMartino platform can reconstruct BLIP screens, drill into discovered items, and build typed normalizers without requiring users to keep opening BLIP.

| Table | Purpose |
| --- | --- |
| `dooh.blip_page_snapshots` | Captures page title, source URL, body text, link count, media count, and raw page metadata. |
| `dooh.blip_discovered_links` | Captures anchors, buttons, labels, hrefs, and inferred target sections discovered during each page scrape. |
| `dooh.blip_media_assets` | Captures image/video/canvas/background assets, dimensions, alt/title text, source page, and raw media metadata. Where the browser allows it, element screenshots are embedded in `raw.screenshot_base64` for creative evidence. |
| `dooh.blip_network_payloads` | Captures REST, GraphQL, and XHR/fetch response payloads observed in the authenticated BLIP browser session. Payload retention is intentionally large enough to preserve schedules, availability, reports, moderation queues, and programmatic data instead of clipping them. |

Long URL indexes use deterministic `md5(...)` expression indexes instead of direct btree indexes so oversized creative/data URLs do not break persistence.

## Detail crawl behavior

After all fixed top-level sections are captured, the runner enqueues same-tenant internal links discovered in the BLIP UI and follows them with a bounded crawler.

- Same authenticated browser session.
- Same BLIP username/password flow.
- No BLIP API key.
- No webhook.
- No bearer token.
- Same Supabase writer.
- Current safety budget: `80` discovered detail pages per run.

The detail crawler captures pages such as moderation tier pages, individual sign detail pages, missing image detail pages, and other internal item routes surfaced by BLIP navigation.

## False-success guard

If login completes but every documented scrape target returns zero rows, the runner fails the sync run and opens a platform incident. This prevents a GitHub Actions green run from being treated as a working BLIP mirror when selectors or BLIP routes have drifted.

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

## Latest verified production run

GitHub Actions run `26135304758` completed successfully after the detail-crawl and payload-capture expansion.

Confirmed connection target:

```text
host=aws-1-us-east-1.pooler.supabase.com port=6543 database=postgres username=postgres.ctmtpjdrsgtnwgwsxmsa ssl=Require
```

Confirmed sync totals from the passing run:

```text
rows_upserted=4572
dashboard=5
plant_signs=14
adkom_availability=19
adkom_holds=11
adkom_contracts=8
adkom_creatives=8
adkom_pop=8
marketplace=5
programmatic_reports=8
page_snapshots=91
detail_pages=74
page_links=2198
media_assets=594
network_payloads=1603
```

## Next backend normalization target

The runner now captures the source material required for full platform parity. The next backend step is typed normalization from the mirror tables into platform-ready domain tables for:

- board space booked vs available,
- Adkom availability, hold, contract, creative, and POP workflows,
- ad moderation approval/deny queues,
- creative image evidence,
- programmatic reporting,
- marketplace analytics,
- report and KPI rollups.
