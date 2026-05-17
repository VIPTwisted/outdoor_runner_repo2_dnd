using BlipSyncAgent.BlipClient;

namespace BlipSyncAgent.Data;

public class SyncProcessor {
    private readonly SupabaseRepository _repo;
    public SyncProcessor(SupabaseRepository repo) { _repo = repo; }

    public async Task RunIncrementalSyncAsync(BlipSession blip) {
        var nav = new BlipNavigator(blip);
        var sc  = new BlipScraper(blip);

        nav.GotoBoards();    await Task.Delay(2500);
        await _repo.UpsertBoardsAsync(sc.ScrapeBoards());

        nav.GotoCampaigns(); await Task.Delay(2500);
        await _repo.UpsertCampaignsAsync(sc.ScrapeCampaigns());

        nav.GotoAds();       await Task.Delay(2500);
        await _repo.UpsertAdsAsync(sc.ScrapeAds());
    }

    public async Task RunFullSyncAsync(BlipSession blip) {
        // For now full == incremental but re-crawled fresh. Extend with deeper pagination
        // / clearing tombstones if you need destructive resync semantics.
        await RunIncrementalSyncAsync(blip);
    }
}
