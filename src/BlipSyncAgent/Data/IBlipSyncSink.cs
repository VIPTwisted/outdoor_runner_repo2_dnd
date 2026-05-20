using BlipSyncAgent.Models;

namespace BlipSyncAgent.Data;

public interface IBlipSyncSink {
    Task LogEventAsync(Guid runId, string level, string phase, string component, string message, object? payload = null);
    Task UpsertDashboardWidgetsAsync(IEnumerable<ScrapedDashboardWidget> rows);
    Task UpsertPlantSignsAsync(IEnumerable<ScrapedPlantSign> rows);
    Task UpsertAdkomAvailabilityAsync(IEnumerable<ScrapedAdkomAvailability> rows);
    Task UpsertAdkomHoldsAsync(IEnumerable<ScrapedAdkomHold> rows);
    Task UpsertAdkomContractsAsync(IEnumerable<ScrapedAdkomContract> rows);
    Task UpsertAdkomCreativesAsync(IEnumerable<ScrapedAdkomCreative> rows);
    Task UpsertAdkomPopAsync(IEnumerable<ScrapedAdkomPop> rows);
    Task UpsertMarketplaceGroupsAsync(IEnumerable<ScrapedNamedRow> rows);
    Task UpsertProgrammaticReportsAsync(IEnumerable<ScrapedNamedRow> rows);
    Task UpsertPageSnapshotsAsync(IEnumerable<ScrapedPageSnapshot> rows);
    Task UpsertPageLinksAsync(IEnumerable<ScrapedPageLink> rows);
    Task UpsertMediaAssetsAsync(IEnumerable<ScrapedMediaAsset> rows);
    Task UpsertNetworkPayloadsAsync(IEnumerable<ScrapedNetworkPayload> rows);
}
