using BlipSyncAgent.BlipClient;

namespace BlipSyncAgent.Data;

public class SyncManifestSummary {
    public int Boards { get; set; }
    public int Campaigns { get; set; }
    public int Ads { get; set; }
    public int RowsUpserted => Boards + Campaigns + Ads;
}

public class SyncProcessor {
    private readonly SupabaseRepository _repo;
    public SyncProcessor(SupabaseRepository repo) { _repo = repo; }

    public async Task<SyncManifestSummary> RunWithForensicsAsync(BlipSession blip, string mode, Guid runId) {
        var m = new SyncManifestSummary();
        var nav = new BlipNavigator(blip);
        var sc  = new BlipScraper(blip);

        await _repo.LogEventAsync(runId, "info", "navigate", "boards", "GET /signs");
        nav.GotoBoards();    await Task.Delay(2500);
        var boards = sc.ScrapeBoards();
        await _repo.LogEventAsync(runId, "info", "scrape", "boards", $"scraped {boards.Count} boards");
        await _repo.UpsertBoardsAsync(boards);
        foreach (var b in boards)
            await _repo.UpsertBoardStatusAsync(b.Id, string.IsNullOrEmpty(b.Status) || b.Status == "ENABLED" ? "ok" : "degraded", null);
        m.Boards = boards.Count;

        await _repo.LogEventAsync(runId, "info", "navigate", "campaigns", "GET /campaigns");
        nav.GotoCampaigns(); await Task.Delay(2500);
        var campaigns = sc.ScrapeCampaigns();
        await _repo.LogEventAsync(runId, "info", "scrape", "campaigns", $"scraped {campaigns.Count} campaigns");
        await _repo.UpsertCampaignsAsync(campaigns);
        m.Campaigns = campaigns.Count;

        await _repo.LogEventAsync(runId, "info", "navigate", "ads", "GET /ads");
        nav.GotoAds();       await Task.Delay(2500);
        var ads = sc.ScrapeAds();
        await _repo.LogEventAsync(runId, "info", "scrape", "ads", $"scraped {ads.Count} ads");
        await _repo.UpsertAdsAsync(ads);
        m.Ads = ads.Count;

        if (mode == "full") await _repo.LogEventAsync(runId, "info", "finalize", "agent", "full resync — extend with deeper pagination here");
        return m;
    }
}
