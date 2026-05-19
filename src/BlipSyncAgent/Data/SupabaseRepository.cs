using BlipSyncAgent.Models;
using Dapper;
using Npgsql;
using System.Text.Json;

namespace BlipSyncAgent.Data;

public class SupabaseRepository : IBlipSyncSink, IDisposable {
    private readonly NpgsqlConnection _conn;
    private Guid? _orgId;

    public SupabaseRepository(string connectionString) {
        _conn = new NpgsqlConnection(connectionString);
        _conn.Open();
    }

    // Atomically claim the next pending row -> status='processing'. Returns null if nothing pending.
    public async Task<(Guid id, string mode)?> ClaimNextPendingAsync() {
        const string sql = @"
            update dooh.sync_requests
               set status='processing', processed_at = now()
             where id = (
               select id from dooh.sync_requests
                where status='pending'
                order by requested_at asc
                for update skip locked
                limit 1
             )
             returning id, kind as mode;";
        var row = await _conn.QueryFirstOrDefaultAsync<dynamic>(sql);
        if (row == null) return null;
        return ((Guid)row.id, (string)row.mode);
    }

    public async Task MarkCompletedAsync(Guid id, string? error = null) {
        const string sql = @"
            update dooh.sync_requests
               set status = case when @error is null then 'completed' else 'failed' end,
                   processed_at = now(),
                   detail = case
                     when @error is null then coalesce(detail, '{}'::jsonb)
                     else coalesce(detail, '{}'::jsonb) || jsonb_build_object('error_message', @error)
                   end
             where id = @id;";
        await _conn.ExecuteAsync(sql, new { id, error });
    }

    public async Task UpsertBoardsAsync(IEnumerable<Board> rows) {
        const string sql = @"
            insert into dooh.boards (org_id, external_id, name, location, status, last_report_at, source_payload, updated_at)
            values (@OrgId, @Id, @Name, @Location, @Status, @LastSeenAt, @Raw::jsonb, now())
            on conflict (org_id, external_id) do update set
              name = excluded.name,
              location = excluded.location,
              status = excluded.status,
              last_report_at = excluded.last_report_at,
              source_payload = excluded.source_payload,
              updated_at = excluded.updated_at;";
        var orgId = await ResolveOrgIdAsync();
        foreach (var r in rows) {
            await _conn.ExecuteAsync(sql, new {
                OrgId = orgId,
                r.Id,
                r.Name,
                r.Location,
                r.Status,
                r.LastSeenAt,
                Raw = r.RawJson ?? "{}"
            });
        }
    }

    public async Task UpsertCampaignsAsync(IEnumerable<Campaign> rows) {
        const string sql = @"
            insert into dooh.campaigns (org_id, external_id, name, advertiser_id, status, start_date, end_date, gross_amount_cents, source_payload, updated_at)
            values (@OrgId, @Id, @Name, @Advertiser, @Status, @StartDate, @EndDate, @BudgetCents, @Raw::jsonb, now())
            on conflict (org_id, external_id) do update set
              name = excluded.name,
              advertiser_id = excluded.advertiser_id,
              status = excluded.status,
              start_date = excluded.start_date,
              end_date = excluded.end_date,
              gross_amount_cents = excluded.gross_amount_cents,
              source_payload = excluded.source_payload,
              updated_at = excluded.updated_at;";
        var orgId = await ResolveOrgIdAsync();
        foreach (var r in rows) {
            await _conn.ExecuteAsync(sql, new {
                OrgId = orgId,
                r.Id,
                r.Name,
                r.Advertiser,
                r.Status,
                r.StartDate,
                r.EndDate,
                BudgetCents = r.Budget.HasValue ? (long?)decimal.Round(r.Budget.Value * 100m, 0) : null,
                Raw = r.RawJson ?? "{}"
            });
        }
    }

    public async Task UpsertAdsAsync(IEnumerable<Ad> rows) {
        const string sql = @"
            insert into dooh.blip_ads (id, name, organization_id, data, blip_payload, captured_at)
            values (@Id, @Name, @OrganizationId, @Data::jsonb, @Raw::jsonb, now())
            on conflict (id) do update set
              name = excluded.name,
              organization_id = excluded.organization_id,
              data = excluded.data,
              blip_payload = excluded.blip_payload,
              captured_at = excluded.captured_at;";
        var orgId = (await ResolveOrgIdAsync()).ToString();
        foreach (var r in rows) {
            await _conn.ExecuteAsync(sql, new {
                r.Id,
                Name = string.IsNullOrWhiteSpace(r.Id) ? "unknown" : r.Id,
                OrganizationId = orgId,
                Data = JsonSerializer.Serialize(new {
                    r.CampaignId,
                    r.BoardId,
                    r.Status,
                    r.LastServedAt
                }),
                Raw = r.RawJson ?? "{}"
            });
        }
    }

    public async Task UpsertDashboardWidgetsAsync(IEnumerable<ScrapedDashboardWidget> rows) {
        const string sql = @"
            insert into dooh.scraped_dashboard_widgets
                (id, section_id, source_url, org, sign, slot_number, ad_group, days_left, raw, processed_at)
            overriding system value
            values
                (@Id, @SectionId, @SourceUrl, @Org, @Sign, @SlotNumber, @AdGroup, @DaysLeft, @RawJson::jsonb, now())
            on conflict (id) do update set
                scraped_at = now(),
                section_id = excluded.section_id,
                source_url = excluded.source_url,
                org = excluded.org,
                sign = excluded.sign,
                slot_number = excluded.slot_number,
                ad_group = excluded.ad_group,
                days_left = excluded.days_left,
                raw = excluded.raw,
                processed_at = excluded.processed_at;";
        foreach (var row in rows) await _conn.ExecuteAsync(sql, row);
    }

    public async Task UpsertPlantSignsAsync(IEnumerable<ScrapedPlantSign> rows) {
        const string scrapedSql = @"
            insert into dooh.scraped_plant_signs
                (id, section_id, source_url, title, subtitle, raw_text, raw, processed_at)
            overriding system value
            values
                (@Id, @SectionId, @SourceUrl, @Title, @Subtitle, @RawText, @RawJson::jsonb, now())
            on conflict (id) do update set
                scraped_at = now(),
                section_id = excluded.section_id,
                source_url = excluded.source_url,
                title = excluded.title,
                subtitle = excluded.subtitle,
                raw_text = excluded.raw_text,
                raw = excluded.raw,
                processed_at = excluded.processed_at;";

        const string normalizedSql = @"
            insert into dooh.blip_plant_signs
                (external_source, external_id, title, subtitle, raw_text, source_section, source_url, raw, raw_jsonb, updated_at)
            values
                ('blip-ui', @ExternalId, @Title, @Subtitle, @RawText, @SectionId, @SourceUrl, @RawJson::jsonb, @RawJson::jsonb, now())
            on conflict (external_source, external_id) do update set
                title = excluded.title,
                subtitle = excluded.subtitle,
                raw_text = excluded.raw_text,
                source_section = excluded.source_section,
                source_url = excluded.source_url,
                raw = excluded.raw,
                raw_jsonb = excluded.raw_jsonb,
                updated_at = excluded.updated_at;";

        foreach (var row in rows) {
            await _conn.ExecuteAsync(scrapedSql, row);
            await _conn.ExecuteAsync(normalizedSql, new {
                ExternalId = row.Id.ToString(),
                row.Title,
                row.Subtitle,
                row.RawText,
                row.SectionId,
                row.SourceUrl,
                row.RawJson
            });
        }
    }

    public async Task UpsertAdkomAvailabilityAsync(IEnumerable<ScrapedAdkomAvailability> rows) {
        const string scrapedSql = @"
            insert into dooh.scraped_adkom_avails
                (id, section_id, source_url, proposal, requested, start_date, end_date, due_date, units, status, raw, processed_at)
            overriding system value
            values
                (@Id, @SectionId, @SourceUrl, @Proposal, @Requested, @StartDate, @EndDate, @DueDate, @Units, @Status, @RawJson::jsonb, now())
            on conflict (id) do update set
                scraped_at = now(),
                section_id = excluded.section_id,
                source_url = excluded.source_url,
                proposal = excluded.proposal,
                requested = excluded.requested,
                start_date = excluded.start_date,
                end_date = excluded.end_date,
                due_date = excluded.due_date,
                units = excluded.units,
                status = excluded.status,
                raw = excluded.raw,
                processed_at = excluded.processed_at;";

        const string normalizedSql = @"
            insert into dooh.blip_avails
                (external_source, external_id, proposal_name, requested_on, start_date, end_date, due_on, units_count, status, source_section, source_url, raw, updated_at)
            values
                ('blip-ui', @ExternalId, @Proposal, @RequestedOn, @StartOn, @EndOn, @DueOn, @UnitsCount, @Status, @SectionId, @SourceUrl, @RawJson::jsonb, now())
            on conflict (external_source, external_id) do update set
                proposal_name = excluded.proposal_name,
                requested_on = excluded.requested_on,
                start_date = excluded.start_date,
                end_date = excluded.end_date,
                due_on = excluded.due_on,
                units_count = excluded.units_count,
                status = excluded.status,
                source_section = excluded.source_section,
                source_url = excluded.source_url,
                raw = excluded.raw,
                updated_at = excluded.updated_at;";

        foreach (var row in rows) {
            await _conn.ExecuteAsync(scrapedSql, row);
            await _conn.ExecuteAsync(normalizedSql, new {
                ExternalId = row.Id.ToString(),
                row.Proposal,
                RequestedOn = ParseDate(row.Requested),
                StartOn = ParseDate(row.StartDate),
                EndOn = ParseDate(row.EndDate),
                DueOn = ParseDate(row.DueDate),
                UnitsCount = ParseInt(row.Units),
                row.Status,
                row.SectionId,
                row.SourceUrl,
                row.RawJson
            });
        }
    }

    public async Task UpsertAdkomHoldsAsync(IEnumerable<ScrapedAdkomHold> rows) {
        const string scrapedSql = @"
            insert into dooh.scraped_adkom_holds
                (id, section_id, source_url, proposal, requested, start_date, end_date, units, proposed_rate, status, raw, processed_at)
            overriding system value
            values
                (@Id, @SectionId, @SourceUrl, @Proposal, @Requested, @StartDate, @EndDate, @Units, @ProposedRate, @Status, @RawJson::jsonb, now())
            on conflict (id) do update set
                scraped_at = now(),
                section_id = excluded.section_id,
                source_url = excluded.source_url,
                proposal = excluded.proposal,
                requested = excluded.requested,
                start_date = excluded.start_date,
                end_date = excluded.end_date,
                units = excluded.units,
                proposed_rate = excluded.proposed_rate,
                status = excluded.status,
                raw = excluded.raw,
                processed_at = excluded.processed_at;";

        const string normalizedSql = @"
            insert into dooh.blip_holds
                (external_source, external_id, proposal_name, requested_on, start_date, end_date, units_count, proposed_rate_cents, status, source_section, source_url, raw, updated_at)
            values
                ('blip-ui', @ExternalId, @Proposal, @RequestedOn, @StartOn, @EndOn, @UnitsCount, @ProposedRateCents, @Status, @SectionId, @SourceUrl, @RawJson::jsonb, now())
            on conflict (external_source, external_id) do update set
                proposal_name = excluded.proposal_name,
                requested_on = excluded.requested_on,
                start_date = excluded.start_date,
                end_date = excluded.end_date,
                units_count = excluded.units_count,
                proposed_rate_cents = excluded.proposed_rate_cents,
                status = excluded.status,
                source_section = excluded.source_section,
                source_url = excluded.source_url,
                raw = excluded.raw,
                updated_at = excluded.updated_at;";

        foreach (var row in rows) {
            await _conn.ExecuteAsync(scrapedSql, row);
            await _conn.ExecuteAsync(normalizedSql, new {
                ExternalId = row.Id.ToString(),
                row.Proposal,
                RequestedOn = ParseDate(row.Requested),
                StartOn = ParseDate(row.StartDate),
                EndOn = ParseDate(row.EndDate),
                UnitsCount = ParseInt(row.Units),
                ProposedRateCents = ParseMoneyCents(row.ProposedRate),
                row.Status,
                row.SectionId,
                row.SourceUrl,
                row.RawJson
            });
        }
    }

    public async Task UpsertAdkomContractsAsync(IEnumerable<ScrapedAdkomContract> rows) {
        const string scrapedSql = @"
            insert into dooh.scraped_adkom_contracts
                (id, section_id, source_url, contract_no, proposal, start_date, end_date, amount, status, units, raw, processed_at)
            overriding system value
            values
                (@Id, @SectionId, @SourceUrl, @ContractNo, @Proposal, @StartDate, @EndDate, @Amount, @Status, @Units, @RawJson::jsonb, now())
            on conflict (id) do update set
                scraped_at = now(),
                section_id = excluded.section_id,
                source_url = excluded.source_url,
                contract_no = excluded.contract_no,
                proposal = excluded.proposal,
                start_date = excluded.start_date,
                end_date = excluded.end_date,
                amount = excluded.amount,
                status = excluded.status,
                units = excluded.units,
                raw = excluded.raw,
                processed_at = excluded.processed_at;";

        const string normalizedSql = @"
            insert into dooh.blip_contracts
                (external_source, external_id, contract_number, proposal_name, start_date, end_date, units_count, amount_cents, status, source_section, source_url, raw, raw_jsonb, updated_at)
            values
                ('blip-ui', @ExternalId, @ContractNo, @Proposal, @StartOn, @EndOn, @UnitsCount, @AmountCents, @Status, @SectionId, @SourceUrl, @RawJson::jsonb, @RawJson::jsonb, now())
            on conflict (external_source, external_id) do update set
                contract_number = excluded.contract_number,
                proposal_name = excluded.proposal_name,
                start_date = excluded.start_date,
                end_date = excluded.end_date,
                units_count = excluded.units_count,
                amount_cents = excluded.amount_cents,
                status = excluded.status,
                source_section = excluded.source_section,
                source_url = excluded.source_url,
                raw = excluded.raw,
                raw_jsonb = excluded.raw_jsonb,
                updated_at = excluded.updated_at;";

        foreach (var row in rows) {
            await _conn.ExecuteAsync(scrapedSql, row);
            await _conn.ExecuteAsync(normalizedSql, new {
                ExternalId = string.IsNullOrWhiteSpace(row.ContractNo) ? row.Id.ToString() : row.ContractNo,
                row.ContractNo,
                row.Proposal,
                StartOn = ParseDate(row.StartDate),
                EndOn = ParseDate(row.EndDate),
                UnitsCount = ParseInt(row.Units),
                AmountCents = ParseMoneyCents(row.Amount),
                row.Status,
                row.SectionId,
                row.SourceUrl,
                row.RawJson
            });
        }
    }

    public async Task UpsertAdkomCreativesAsync(IEnumerable<ScrapedAdkomCreative> rows) {
        const string sql = @"
            insert into dooh.scraped_adkom_creatives
                (id, section_id, source_url, proposal, review_date, creatives, raw, processed_at)
            overriding system value
            values
                (@Id, @SectionId, @SourceUrl, @Proposal, @ReviewDate, @Creatives, @RawJson::jsonb, now())
            on conflict (id) do update set
                scraped_at = now(),
                section_id = excluded.section_id,
                source_url = excluded.source_url,
                proposal = excluded.proposal,
                review_date = excluded.review_date,
                creatives = excluded.creatives,
                raw = excluded.raw,
                processed_at = excluded.processed_at;";
        foreach (var row in rows) await _conn.ExecuteAsync(sql, row);
    }

    public async Task UpsertAdkomPopAsync(IEnumerable<ScrapedAdkomPop> rows) {
        const string scrapedSql = @"
            insert into dooh.scraped_adkom_pop
                (id, section_id, source_url, proposal, pop_date, creatives, raw, processed_at)
            overriding system value
            values
                (@Id, @SectionId, @SourceUrl, @Proposal, @PopDate, @Creatives, @RawJson::jsonb, now())
            on conflict (id) do update set
                scraped_at = now(),
                section_id = excluded.section_id,
                source_url = excluded.source_url,
                proposal = excluded.proposal,
                pop_date = excluded.pop_date,
                creatives = excluded.creatives,
                raw = excluded.raw,
                processed_at = excluded.processed_at;";

        const string normalizedSql = @"
            insert into dooh.blip_pop_reports
                (external_source, external_id, proposal_name, pop_date, creatives_count, source_section, source_url, raw, updated_at)
            values
                ('blip-ui', @ExternalId, @Proposal, @PopOn, @CreativesCount, @SectionId, @SourceUrl, @RawJson::jsonb, now())
            on conflict (external_source, external_id) do update set
                proposal_name = excluded.proposal_name,
                pop_date = excluded.pop_date,
                creatives_count = excluded.creatives_count,
                source_section = excluded.source_section,
                source_url = excluded.source_url,
                raw = excluded.raw,
                updated_at = excluded.updated_at;";

        foreach (var row in rows) {
            await _conn.ExecuteAsync(scrapedSql, row);
            await _conn.ExecuteAsync(normalizedSql, new {
                ExternalId = row.Id.ToString(),
                row.Proposal,
                PopOn = ParseDate(row.PopDate),
                CreativesCount = ParseInt(row.Creatives),
                row.SectionId,
                row.SourceUrl,
                row.RawJson
            });
        }
    }

    public async Task UpsertMarketplaceGroupsAsync(IEnumerable<ScrapedNamedRow> rows) {
        const string sql = @"
            insert into dooh.scraped_marketplace_groups
                (id, section_id, source_url, group_name, raw, processed_at)
            overriding system value
            values
                (@Id, @SectionId, @SourceUrl, @Name, @RawJson::jsonb, now())
            on conflict (id) do update set
                scraped_at = now(),
                section_id = excluded.section_id,
                source_url = excluded.source_url,
                group_name = excluded.group_name,
                raw = excluded.raw,
                processed_at = excluded.processed_at;";
        foreach (var row in rows) await _conn.ExecuteAsync(sql, row);
    }

    public async Task UpsertProgrammaticReportsAsync(IEnumerable<ScrapedNamedRow> rows) {
        const string sql = @"
            insert into dooh.scraped_programmatic_reports
                (id, section_id, source_url, report_name, description, created, status, format, raw, processed_at)
            overriding system value
            values
                (@Id, @SectionId, @SourceUrl, @Name, @Description, @Created, @Status, @Format, @RawJson::jsonb, now())
            on conflict (id) do update set
                scraped_at = now(),
                section_id = excluded.section_id,
                source_url = excluded.source_url,
                report_name = excluded.report_name,
                description = excluded.description,
                created = excluded.created,
                status = excluded.status,
                format = excluded.format,
                raw = excluded.raw,
                processed_at = excluded.processed_at;";
        foreach (var row in rows) await _conn.ExecuteAsync(sql, row);
    }

    public async Task<Guid> StartSyncRunAsync(string mode, string runner, string? workflowRunId, Guid? requestId) {
        const string sql = @"
            insert into dooh.sync_runs (org_id, external_id, source, status, started_at, source_payload)
            values (@orgId, @externalId, 'blip-sync-agent', 'running', now(), @payload::jsonb)
            on conflict (org_id, external_id) do update set
              status = excluded.status,
              started_at = excluded.started_at,
              finished_at = null,
              source_payload = excluded.source_payload,
              updated_at = now()
            returning id;";
        var orgId = await ResolveOrgIdAsync();
        var externalId = string.IsNullOrWhiteSpace(workflowRunId)
            ? $"local-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}"
            : $"github-{workflowRunId}-{Guid.NewGuid():N}";
        return await _conn.ExecuteScalarAsync<Guid>(sql, new {
            orgId,
            externalId,
            payload = JsonSerializer.Serialize(new {
                mode,
                runner,
                workflow_run_id = workflowRunId,
                request_id = requestId
            })
        });
    }

    public async Task FinishSyncRunAsync(Guid runId, string status, string? error, int rowsUpserted, object? manifest) {
        const string sql = @"
            update dooh.sync_runs
               set status = @status,
                   finished_at = now(),
                   source_payload = coalesce(source_payload, '{}'::jsonb) || @payload::jsonb,
                   updated_at = now()
             where id = @id;";
        await _conn.ExecuteAsync(sql, new {
            id = runId,
            status,
            payload = JsonSerializer.Serialize(new {
                error_message = error,
                rows_upserted = rowsUpserted,
                manifest = manifest ?? new {}
            })
        });
    }

    public async Task LogEventAsync(Guid runId, string level, string phase, string component, string message, object? payload = null) {
        const string sql = @"
            insert into dooh.sync_events (sync_run_id, level, component, message, context)
            values (@rid, @lvl, @comp, @msg, @payload::jsonb);";
        await _conn.ExecuteAsync(sql, new {
            rid = runId,
            lvl = level,
            comp = component,
            msg = message,
            payload = JsonSerializer.Serialize(new {
                phase,
                payload = payload ?? new {}
            })
        });
    }

    public async Task UpsertBoardStatusAsync(string signId, string state, string? errorText) {
        const string sql = @"
            insert into dooh.board_status (board_id, status, last_seen_at, last_successful_sync_at, last_error, updated_at)
            values (@id, @state, now(), case when @state = 'ok' then now() else null end, @err, now())
            on conflict (board_id) do update set
                status = excluded.status,
                last_seen_at = excluded.last_seen_at,
                last_successful_sync_at = coalesce(excluded.last_successful_sync_at, dooh.board_status.last_successful_sync_at),
                last_error = excluded.last_error,
                updated_at = excluded.updated_at;";
        await _conn.ExecuteAsync(sql, new { id = signId, state, err = errorText });
    }

    public async Task OpenIncidentAsync(string kind, string severity, string? subjectId, string summary, object? detail = null) {
        const string sql = @"
            insert into dooh.platform_incidents (source, severity, title, description, context)
            values (@kind, @sev, @summary, @description, @detail::jsonb);";
        await _conn.ExecuteAsync(sql, new {
            kind,
            sev = severity,
            summary,
            description = subjectId,
            detail = JsonSerializer.Serialize(detail ?? new {})
        });
    }

    private async Task<Guid> ResolveOrgIdAsync() {
        if (_orgId.HasValue) return _orgId.Value;

        const string sql = @"
            select org_id
            from dooh.organizations
            order by case when org_id = '00000000-0000-0000-0000-000000000001'::uuid then 1 else 0 end,
                     created_at nulls last
            limit 1;";
        _orgId = await _conn.ExecuteScalarAsync<Guid?>(sql)
            ?? throw new InvalidOperationException("No dooh.organizations row exists; cannot write org-scoped BLIP sync data.");
        return _orgId.Value;
    }

    private static DateTime? ParseDate(string? value) {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTime.TryParse(value, out var parsed) ? parsed.Date : null;
    }

    private static int? ParseInt(string? value) {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var parsed) ? parsed : null;
    }

    private static long? ParseMoneyCents(string? value) {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = new string(value.Where(ch => char.IsDigit(ch) || ch == '.' || ch == '-').ToArray());
        return decimal.TryParse(normalized, out var parsed) ? (long?)decimal.Round(parsed * 100m, 0) : null;
    }

    public void Dispose() {
        try { _conn.Close(); } catch { }
        _conn.Dispose();
    }
}
