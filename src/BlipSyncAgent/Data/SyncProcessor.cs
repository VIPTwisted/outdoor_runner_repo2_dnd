using BlipSyncAgent.BlipClient;
using BlipSyncAgent.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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
    public int PageSnapshots { get; set; }
    public int PageLinks { get; set; }
    public int MediaAssets { get; set; }
    public int NetworkPayloads { get; set; }

    public int RowsUpserted =>
        DashboardWidgets +
        PlantSigns +
        AdkomAvailability +
        AdkomHolds +
        AdkomContracts +
        AdkomCreatives +
        AdkomPop +
        MarketplaceGroups +
        ProgrammaticReports +
        PageSnapshots +
        PageLinks +
        MediaAssets +
        NetworkPayloads;
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

        await CaptureAsync("dashboard", "GET /dashboard", nav.GotoDashboard, scraper.ScrapeDashboardWidgets, _sink.UpsertDashboardWidgetsAsync, count => manifest.DashboardWidgets = count, manifest, runId, network, scraper);
        await CaptureAsync("plant-signs", "GET /signs/signs", nav.GotoPlantSigns, scraper.ScrapePlantSigns, _sink.UpsertPlantSignsAsync, count => manifest.PlantSigns = count, manifest, runId, network, scraper);
        await MirrorPageOnlyAsync("plant-preview", "GET /signs/preview", nav.GotoPlantPreview, manifest, runId, network, scraper);
        await MirrorPageOnlyAsync("plant-ads", "GET /signs/ads", nav.GotoPlantAds, manifest, runId, network, scraper);
        await MirrorPageOnlyAsync("image-verification", "GET /signs/image-verification", nav.GotoImageVerification, manifest, runId, network, scraper);
        await MirrorPageOnlyAsync("slot-assignments", "GET /signs/assignments", nav.GotoAssignments, manifest, runId, network, scraper);
        await MirrorPageOnlyAsync("advertisers", "GET /signs/advertiser", nav.GotoAdvertisers, manifest, runId, network, scraper);
        await MirrorPageOnlyAsync("plant-reports", "GET /signs/reports", nav.GotoPlantReports, manifest, runId, network, scraper);
        await MirrorPageOnlyAsync("campaigns", "GET /campaigns", nav.GotoCampaigns, manifest, runId, network, scraper);
        await MirrorPageOnlyAsync("ads", "GET /ads", nav.GotoAds, manifest, runId, network, scraper);
        await CaptureAsync("adkom-availability", "GET /adkom/available", nav.GotoAdkomAvailability, scraper.ScrapeAdkomAvailability, _sink.UpsertAdkomAvailabilityAsync, count => manifest.AdkomAvailability = count, manifest, runId, network, scraper);
        await CaptureAsync("adkom-holds", "GET /adkom/holds", nav.GotoAdkomHolds, scraper.ScrapeAdkomHolds, _sink.UpsertAdkomHoldsAsync, count => manifest.AdkomHolds = count, manifest, runId, network, scraper);
        await CaptureAsync("adkom-contracts", "GET /adkom/contracts", nav.GotoAdkomContracts, scraper.ScrapeAdkomContracts, _sink.UpsertAdkomContractsAsync, count => manifest.AdkomContracts = count, manifest, runId, network, scraper);
        await CaptureAsync("adkom-creatives", "GET /adkom/creatives", nav.GotoAdkomCreatives, scraper.ScrapeAdkomCreatives, _sink.UpsertAdkomCreativesAsync, count => manifest.AdkomCreatives = count, manifest, runId, network, scraper);
        await CaptureAsync("adkom-pop", "GET /adkom/pops", nav.GotoAdkomPop, scraper.ScrapeAdkomPop, _sink.UpsertAdkomPopAsync, count => manifest.AdkomPop = count, manifest, runId, network, scraper);
        await CaptureAsync("marketplace", "GET /organizations/marketplace-analytics", nav.GotoMarketplace, scraper.ScrapeMarketplaceGroups, _sink.UpsertMarketplaceGroupsAsync, count => manifest.MarketplaceGroups = count, manifest, runId, network, scraper);
        await CaptureAsync("programmatic-reports", "GET /organizations/programmatic/reports", nav.GotoProgrammaticReports, scraper.ScrapeProgrammaticReports, _sink.UpsertProgrammaticReportsAsync, count => manifest.ProgrammaticReports = count, manifest, runId, network, scraper);

        if (manifest.RowsUpserted == 0) {
            throw new InvalidOperationException("BLIP login completed, but every documented scrape target returned zero rows. Treating this as a failed sync to prevent false-success drift.");
        }

        return manifest;
    }

    private async Task MirrorPageOnlyAsync(
        string component,
        string navigationMessage,
        Action navigate,
        SyncManifestSummary manifest,
        Guid runId,
        BlipNetworkRecorder network,
        BlipScraper scraper
    ) {
        await CaptureAsync<object>(
            component,
            navigationMessage,
            navigate,
            () => new List<object>(),
            _ => Task.CompletedTask,
            _ => { },
            manifest,
            runId,
            network,
            scraper,
            writeEmptyDiagnostics: false
        );
    }

    private async Task CaptureAsync<T>(
        string component,
        string navigationMessage,
        Action navigate,
        Func<List<T>> scrape,
        Func<IEnumerable<T>, Task> persist,
        Action<int> assignCount,
        SyncManifestSummary manifest,
        Guid runId,
        BlipNetworkRecorder network,
        BlipScraper scraper,
        bool writeEmptyDiagnostics = true
    ) {
        await _sink.LogEventAsync(runId, "info", "navigate", component, navigationMessage);
        network.Clear();
        navigate();
        await Task.Delay(2500);

        var rows = scrape();
        var networkEvents = network.Read();
        network.WriteDiagnostics(component, networkEvents);
        assignCount(rows.Count);
        await PersistMirrorArtifactsAsync(component, scraper, networkEvents, manifest);

        var restCount = networkEvents.Count(evt => evt.Url.Contains("/api/", StringComparison.OrdinalIgnoreCase));
        var graphCount = networkEvents.Count(evt => evt.Url.Contains("/gql", StringComparison.OrdinalIgnoreCase) || evt.Url.Contains("graphql", StringComparison.OrdinalIgnoreCase));
        await _sink.LogEventAsync(runId, "info", "scrape", component, $"scraped {rows.Count} rows; network events={networkEvents.Count}; rest={restCount}; graphql={graphCount}");
        if (rows.Count > 0) {
            await persist(rows);
            await _sink.LogEventAsync(runId, "info", "persist", component, $"upserted {rows.Count} rows");
        } else if (writeEmptyDiagnostics) {
            scraper.WriteDiagnostics(component);
        }
    }

    private async Task PersistMirrorArtifactsAsync(string sectionId, BlipScraper scraper, IReadOnlyList<BlipNetworkEvent> networkEvents, SyncManifestSummary manifest) {
        var snapshot = scraper.ScrapePageSnapshot(sectionId);
        var links = scraper.ScrapeLinks(sectionId);
        var media = scraper.ScrapeMediaAssets(sectionId);
        var payloads = networkEvents
            .Select(evt => ToPayload(sectionId, snapshot.SourceUrl, evt))
            .ToList();

        await _sink.UpsertPageSnapshotsAsync(new[] { snapshot });
        await _sink.UpsertPageLinksAsync(links);
        await _sink.UpsertMediaAssetsAsync(media);
        await _sink.UpsertNetworkPayloadsAsync(payloads);

        manifest.PageSnapshots += 1;
        manifest.PageLinks += links.Count;
        manifest.MediaAssets += media.Count;
        manifest.NetworkPayloads += payloads.Count;
    }

    private static ScrapedNetworkPayload ToPayload(string sectionId, string sourceUrl, BlipNetworkEvent evt) {
        var rawJson = JsonSerializer.Serialize(evt);
        return new ScrapedNetworkPayload {
            Id = StablePositiveId($"network|{sectionId}|{sourceUrl}|{evt.Kind}|{evt.Method}|{evt.Url}|{evt.Status}|{evt.BodyPreview}"),
            SectionId = sectionId,
            SourceUrl = sourceUrl,
            Kind = evt.Kind,
            Method = evt.Method,
            Url = evt.Url,
            Status = evt.Status,
            ContentType = evt.ContentType,
            BodyPreview = evt.BodyPreview,
            RawJson = rawJson
        };
    }

    private static long StablePositiveId(string input) {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var value = BitConverter.ToInt64(bytes, 0) & long.MaxValue;
        return value == 0 ? 1 : value;
    }
}
