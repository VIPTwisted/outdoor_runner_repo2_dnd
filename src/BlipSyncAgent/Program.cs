using BlipSyncAgent;
using BlipSyncAgent.BlipClient;
using BlipSyncAgent.Data;

try {
    var cfg = new Config();
    Console.WriteLine($"[BlipSyncAgent] start  slug={cfg.BlipOperatorSlug}  user={cfg.BlipUsername}");

    using var repo = new SupabaseRepository(cfg.PostgresConnectionString);

    // Drain ALL pending requests this invocation. If none, also run a free incremental
    // sync to keep data fresh on the 1-minute cadence.
    int processed = 0;
    Guid? lastClaimedId = null;
    while (true) {
        var claimed = await repo.ClaimNextPendingAsync();
        if (claimed == null) break;
        var (id, mode) = claimed.Value;
        Console.WriteLine($"[BlipSyncAgent] claimed sync_request id={id} mode={mode}");
        lastClaimedId = id;

        try {
            using var blip = new BlipSession(cfg);
            await blip.LoginAsync();
            var proc = new SyncProcessor(repo);
            if (mode == "full") await proc.RunFullSyncAsync(blip);
            else                await proc.RunIncrementalSyncAsync(blip);
            await repo.MarkCompletedAsync(id);
            processed++;
        } catch (Exception ex) {
            Console.Error.WriteLine($"[BlipSyncAgent] sync failed id={id}: {ex.Message}");
            await repo.MarkCompletedAsync(id, error: ex.ToString());
        }
    }

    if (processed == 0) {
        // No queued button-clicks. Run an unconditional incremental sync to satisfy the
        // every-1-minute freshness goal.
        Console.WriteLine("[BlipSyncAgent] no pending requests — running heartbeat incremental sync");
        using var blip = new BlipSession(cfg);
        await blip.LoginAsync();
        var proc = new SyncProcessor(repo);
        await proc.RunIncrementalSyncAsync(blip);
    }

    Console.WriteLine($"[BlipSyncAgent] done. processed={processed}");
    return 0;
} catch (Exception ex) {
    Console.Error.WriteLine("[BlipSyncAgent] FATAL: " + ex);
    return 1;
}
