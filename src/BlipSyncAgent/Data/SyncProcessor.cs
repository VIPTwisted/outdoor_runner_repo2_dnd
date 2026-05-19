using BlipSyncAgent.BlipClient;

namespace BlipSyncAgent.Data;

public sealed class SyncManifestSummary {
    public int DashboardWidgets { get; set; }
    public int PlantSigns { get; set; }
    public int AdkomAvailability { get; set; }
    public int AdkomHolds { get; set; }
    public int AdkomContracts { get; set; }
    public int AdkomCreatives { get; set; }
    public int AdkomPop { get; set; }
    public int MarketplaceGroups { get; set; }
    public int ProgrammaticReports { get; set; }

    public int RowsUpserted =>
        DashboardWidgets +
        PlantSigns +
        AdkomAvailability +
        AdkomHolds +
        AdkomContracts +
        AdkomCreatives +
        AdkomPop +
        MarketplaceGroups +
        ProgrammaticReports;
}

public sealed class SyncProcessor {
    private readonly IBlipSyncSink _sink;
    public SyncProcessor(IBlipSyncSink sink) { _sink = sink; }

    public async Task<SyncManifestSummary> RunWithForensicsAsync(BlipSession blip, string mode, Guid runId) {
        var manifest = new SyncManifestSummary();
        var nav = new BlipNavigator(blip);
        var scraper = new BlipScraper(blip);
        var network = new BlipNetworkRecorder(blip);
        network.Install();

        await CaptureAsync("dashboard", "GET /dashboard", nav.GotoDashboard, scraper.ScrapeDashboardWidgets, _sink.UpsertDashboardWidgetsAsync, count => manifest.DashboardWidgets = count, runId, network);
        await CaptureAsync("plant-signs", "GET /signs/signs", nav.GotoPlantSigns, scraper.ScrapePlantSigns, _sink.UpsertPlantSignsAsync, count => manifest.PlantSigns = count, runId, network);
        await CaptureAsync("adkom-availability", "GET /adkom/available", nav.GotoAdkomAvailability, scraper.ScrapeAdkomAvailability, _sink.UpsertAdkomAvailabilityAsync, count => manifest.AdkomAvailability = count, runId, network);
        await CaptureAsync("adkom-holds", "GET /adkom/holds", nav.GotoAdkomHolds, scraper.ScrapeAdkomHolds, _sink.UpsertAdkomHoldsAsync, count => manifest.AdkomHolds = count, runId, network);
        await CaptureAsync("adkom-contracts", "GET /adkom/contracts", nav.GotoAdkomContracts, scraper.ScrapeAdkomContracts, _sink.UpsertAdkomContractsAsync, count => manifest.AdkomContracts = count, runId, network);
        await CaptureAsync("adkom-creatives", "GET /adkom/creatives", nav.GotoAdkomCreatives, scraper.ScrapeAdkomCreatives, _sink.UpsertAdkomCreativesAsync, count => manifest.AdkomCreatives = count, runId, network);
        await CaptureAsync("adkom-pop", "GET /adkom/pops", nav.GotoAdkomPop, scraper.ScrapeAdkomPop, _sink.UpsertAdkomPopAsync, count => manifest.AdkomPop = count, runId, network);
        await CaptureAsync("marketplace", "GET /organizations/marketplace-analytics", nav.GotoMarketplace, scraper.ScrapeMarketplaceGroups, _sink.UpsertMarketplaceGroupsAsync, count => manifest.MarketplaceGroups = count, runId, network);
        await CaptureAsync("programmatic-reports", "GET /organizations/programmatic/reports", nav.GotoProgrammaticReports, scraper.ScrapeProgrammaticReports, _sink.UpsertProgrammaticReportsAsync, count => manifest.ProgrammaticReports = count, runId, network);

        if (manifest.RowsUpserted == 0) {
            throw new InvalidOperationException("BLIP login completed, but every documented scrape target returned zero rows. Treating this as a failed sync to prevent false-success drift.");
        }

        return manifest;
    }

    private async Task CaptureAsync<T>(
        string component,
        string navigationMessage,
        Action navigate,
        Func<List<T>> scrape,
        Func<IEnumerable<T>, Task> persist,
        Action<int> assignCount,
        Guid runId,
        BlipNetworkRecorder network
    ) {
        await _sink.LogEventAsync(runId, "info", "navigate", component, navigationMessage);
        network.Clear();
        navigate();
        await Task.Delay(2500);

        var rows = scrape();
        var networkEvents = network.Read();
        network.WriteDiagnostics(component, networkEvents);
        assignCount(rows.Count);

        var restCount = networkEvents.Count(evt => evt.Url.Contains("/api/", StringComparison.OrdinalIgnoreCase));
        var graphCount = networkEvents.Count(evt => evt.Url.Contains("/gql", StringComparison.OrdinalIgnoreCase) || evt.Url.Contains("graphql", StringComparison.OrdinalIgnoreCase));
        await _sink.LogEventAsync(runId, "info", "scrape", component, $"scraped {rows.Count} rows; network events={networkEvents.Count}; rest={restCount}; graphql={graphCount}");
        if (rows.Count > 0) {
            await persist(rows);
            await _sink.LogEventAsync(runId, "info", "persist", component, $"upserted {rows.Count} rows");
        } else if (scrape.Target is BlipScraper scraper) {
            scraper.WriteDiagnostics(component);
        }
    }
}
