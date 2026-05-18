using BlipSyncAgent.Models;
using Dapper;
using Npgsql;
using System.Text.Json;

namespace BlipSyncAgent.Data;

public class SupabaseRepository : IDisposable {
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

    public void Dispose() {
        try { _conn.Close(); } catch { }
        _conn.Dispose();
    }
}
