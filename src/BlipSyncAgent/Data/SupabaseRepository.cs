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

    public void Dispose() { try { _conn.Close(); } catch { } _conn.Dispose(); }
}
