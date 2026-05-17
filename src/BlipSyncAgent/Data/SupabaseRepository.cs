using BlipSyncAgent.Models;
using Dapper;
using Npgsql;
using System.Text.Json;

namespace BlipSyncAgent.Data;

public class SupabaseRepository : IDisposable {
    private readonly NpgsqlConnection _conn;
    public SupabaseRepository(string connectionString) {
        _conn = new NpgsqlConnection(connectionString);
        _conn.Open();
    }

    // Atomically claim the next pending row → status='processing'. Returns null if nothing pending.
    public async Task<(Guid id, string mode)?> ClaimNextPendingAsync() {
        const string sql = @"
            update dooh.sync_requests
               set status='processing', started_at = now()
             where id = (
               select id from dooh.sync_requests
                where status='pending'
                order by created_at asc
                for update skip locked
                limit 1
             )
             returning id, mode;";
        var row = await _conn.QueryFirstOrDefaultAsync<dynamic>(sql);
        if (row == null) return null;
        return ((Guid)row.id, (string)row.mode);
    }

    public async Task MarkCompletedAsync(Guid id, string? error = null) {
        const string sql = @"
            update dooh.sync_requests
               set status = case when @error is null then 'completed' else 'failed' end,
                   completed_at = now(),
                   error_message = @error
             where id = @id;";
        await _conn.ExecuteAsync(sql, new { id, error });
    }

    public async Task UpsertBoardsAsync(IEnumerable<Board> rows) {
        const string sql = @"
            insert into dooh.blip_boards (id, name, location, status, last_seen_at, raw, captured_at)
            values (@Id, @Name, @Location, @Status, @LastSeenAt, @Raw::jsonb, now())
            on conflict (id) do update set
              name = excluded.name,
              location = excluded.location,
              status = excluded.status,
              last_seen_at = excluded.last_seen_at,
              raw = excluded.raw,
              captured_at = excluded.captured_at;";
        foreach (var r in rows) {
            await _conn.ExecuteAsync(sql, new {
                r.Id, r.Name, r.Location, r.Status, r.LastSeenAt,
                Raw = r.RawJson ?? "{}"
            });
        }
    }

    public async Task UpsertCampaignsAsync(IEnumerable<Campaign> rows) {
        const string sql = @"
            insert into dooh.blip_campaigns (id, name, advertiser, status, start_date, end_date, budget, raw, captured_at)
            values (@Id, @Name, @Advertiser, @Status, @StartDate, @EndDate, @Budget, @Raw::jsonb, now())
            on conflict (id) do update set
              name = excluded.name,
              advertiser = excluded.advertiser,
              status = excluded.status,
              start_date = excluded.start_date,
              end_date = excluded.end_date,
              budget = excluded.budget,
              raw = excluded.raw,
              captured_at = excluded.captured_at;";
        foreach (var r in rows) {
            await _conn.ExecuteAsync(sql, new {
                r.Id, r.Name, r.Advertiser, r.Status, r.StartDate, r.EndDate, r.Budget,
                Raw = r.RawJson ?? "{}"
            });
        }
    }

    public async Task UpsertAdsAsync(IEnumerable<Ad> rows) {
        const string sql = @"
            insert into dooh.blip_ads (id, campaign_id, board_id, status, last_served_at, raw, captured_at)
            values (@Id, @CampaignId, @BoardId, @Status, @LastServedAt, @Raw::jsonb, now())
            on conflict (id) do update set
              campaign_id = excluded.campaign_id,
              board_id = excluded.board_id,
              status = excluded.status,
              last_served_at = excluded.last_served_at,
              raw = excluded.raw,
              captured_at = excluded.captured_at;";
        foreach (var r in rows) {
            await _conn.ExecuteAsync(sql, new {
                r.Id, r.CampaignId, r.BoardId, r.Status, r.LastServedAt,
                Raw = r.RawJson ?? "{}"
            });
        }
    }

    // ─── Forensic logging hooks ─────────────────────────────────────
    public async Task<Guid> StartSyncRunAsync(string mode, string runner, string? workflowRunId, Guid? requestId) {
        const string sql = @"
            insert into dooh.sync_runs (mode, runner, workflow_run_id, request_id, status, started_at)
            values (@mode, @runner, @wfid, @rid, 'running', now())
            returning id;";
        return await _conn.ExecuteScalarAsync<Guid>(sql, new {
            mode, runner, wfid = workflowRunId, rid = (object?)requestId ?? DBNull.Value
        });
    }
    public async Task FinishSyncRunAsync(Guid runId, string status, string? error, int rowsUpserted, object? manifest) {
        const string sql = @"
            update dooh.sync_runs
               set status = @status,
                   error_message = @error,
                   finished_at = now(),
                   duration_ms = (extract(epoch from (now() - started_at)) * 1000)::int,
                   rows_upserted = @rows,
                   manifest = @manifest::jsonb
             where id = @id;";
        await _conn.ExecuteAsync(sql, new {
            id = runId, status, error,
            rows = rowsUpserted,
            manifest = JsonSerializer.Serialize(manifest ?? new {})
        });
    }
    public async Task LogEventAsync(Guid runId, string level, string phase, string component, string message, object? payload = null) {
        const string sql = @"
            insert into dooh.sync_events (run_id, level, phase, component, message, payload)
            values (@rid, @lvl, @phase, @comp, @msg, @payload::jsonb);";
        await _conn.ExecuteAsync(sql, new {
            rid = runId, lvl = level, phase, comp = component, msg = message,
            payload = JsonSerializer.Serialize(payload ?? new {})
        });
    }
    public async Task UpsertBoardStatusAsync(string signId, string state, string? errorText) {
        const string sql = @"
            insert into dooh.board_status (sign_id, state, last_seen_at, last_successful_sync_at, error_text, updated_at)
            values (@id, @state, now(), case when @state = 'ok' then now() else null end, @err, now())
            on conflict (sign_id) do update set
                state = excluded.state,
                last_seen_at = excluded.last_seen_at,
                last_successful_sync_at = coalesce(excluded.last_successful_sync_at, dooh.board_status.last_successful_sync_at),
                error_text = excluded.error_text,
                updated_at = excluded.updated_at;";
        await _conn.ExecuteAsync(sql, new { id = signId, state, err = errorText });
    }
    public async Task OpenIncidentAsync(string kind, string severity, string? subjectId, string summary, object? detail = null) {
        const string sql = @"
            insert into dooh.platform_incidents (kind, severity, subject_id, summary, detail)
            values (@kind, @sev, @sid, @summary, @detail::jsonb);";
        await _conn.ExecuteAsync(sql, new {
            kind, sev = severity, sid = subjectId, summary,
            detail = JsonSerializer.Serialize(detail ?? new {})
        });
    }

    public void Dispose() { try { _conn.Close(); } catch { } _conn.Dispose(); }
}
