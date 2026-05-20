using BlipSyncAgent.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BlipSyncAgent.Data;

public sealed class ArtifactSyncSink : IBlipSyncSink {
    private static readonly Regex SafeName = new(@"[^a-zA-Z0-9_.-]+", RegexOptions.Compiled);
    private readonly string _root;

    public ArtifactSyncSink(string root = "blip-diagnostics") {
        _root = root;
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "data"));
    }

    public Task LogEventAsync(Guid runId, string level, string phase, string component, string message, object? payload = null) {
        var evt = new {
            run_id = runId,
            at = DateTimeOffset.UtcNow,
            level,
            phase,
            component,
            message,
            payload = payload ?? new {}
        };
        AppendJsonLine(Path.Combine(_root, "events.jsonl"), evt);
        Console.WriteLine($"[artifact-sink] {level} {phase}/{component}: {message}");
        return Task.CompletedTask;
    }

    public Task UpsertDashboardWidgetsAsync(IEnumerable<ScrapedDashboardWidget> rows) => WriteRows("scraped_dashboard_widgets", rows);
    public Task UpsertPlantSignsAsync(IEnumerable<ScrapedPlantSign> rows) => WriteRows("scraped_plant_signs", rows);
    public Task UpsertAdkomAvailabilityAsync(IEnumerable<ScrapedAdkomAvailability> rows) => WriteRows("scraped_adkom_avails", rows);
    public Task UpsertAdkomHoldsAsync(IEnumerable<ScrapedAdkomHold> rows) => WriteRows("scraped_adkom_holds", rows);
    public Task UpsertAdkomContractsAsync(IEnumerable<ScrapedAdkomContract> rows) => WriteRows("scraped_adkom_contracts", rows);
    public Task UpsertAdkomCreativesAsync(IEnumerable<ScrapedAdkomCreative> rows) => WriteRows("scraped_adkom_creatives", rows);
    public Task UpsertAdkomPopAsync(IEnumerable<ScrapedAdkomPop> rows) => WriteRows("scraped_adkom_pop", rows);
    public Task UpsertMarketplaceGroupsAsync(IEnumerable<ScrapedNamedRow> rows) => WriteRows("scraped_marketplace_groups", rows);
    public Task UpsertProgrammaticReportsAsync(IEnumerable<ScrapedNamedRow> rows) => WriteRows("scraped_programmatic_reports", rows);
    public Task UpsertPageSnapshotsAsync(IEnumerable<ScrapedPageSnapshot> rows) => WriteRows("blip_page_snapshots", rows);
    public Task UpsertPageLinksAsync(IEnumerable<ScrapedPageLink> rows) => WriteRows("blip_discovered_links", rows);
    public Task UpsertMediaAssetsAsync(IEnumerable<ScrapedMediaAsset> rows) => WriteRows("blip_media_assets", rows);
    public Task UpsertNetworkPayloadsAsync(IEnumerable<ScrapedNetworkPayload> rows) => WriteRows("blip_network_payloads", rows);

    private Task WriteRows<T>(string tableName, IEnumerable<T> rows) {
        var materialized = rows.ToList();
        var safeName = SafeName.Replace(tableName, "-").Trim('-');
        var path = Path.Combine(_root, "data", $"{safeName}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(materialized, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"[artifact-sink] wrote {materialized.Count} rows to {path}");
        return Task.CompletedTask;
    }

    private static void AppendJsonLine(string path, object value) {
        File.AppendAllText(path, JsonSerializer.Serialize(value) + Environment.NewLine);
    }
}
