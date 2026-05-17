using BlipSyncAgent;
using BlipSyncAgent.BlipClient;
using BlipSyncAgent.Data;

try {
    var cfg = new Config();
    var workflowRunId = Environment.GetEnvironmentVariable("GITHUB_RUN_ID");
    var runnerKind    = string.IsNullOrEmpty(workflowRunId) ? "local" : "github-actions";
    Console.WriteLine($"[BlipSyncAgent] start  slug={cfg.BlipOperatorSlug}  runner={runnerKind}  wfRun={workflowRunId}");

    using var repo = new SupabaseRepository(cfg.PostgresConnectionString);

    async Task RunOnePassAsync(string mode, Guid? requestId) {
        var runId = await repo.StartSyncRunAsync(mode, runnerKind, workflowRunId, requestId);
        await repo.LogEventAsync(runId, "info", "init", "agent", $"pass started mode={mode}");
        int rows = 0;
        try {
            using var blip = new BlipSession(cfg);
            await repo.LogEventAsync(runId, "info", "login", "agent", "browser launched");
            await blip.LoginAsync();
            await repo.LogEventAsync(runId, "info", "login", "agent", "login complete");
            var proc = new SyncProcessor(repo);
            var manifest = await proc.RunWithForensicsAsync(blip, mode, runId);
            rows = manifest.RowsUpserted;
            await repo.LogEventAsync(runId, "info", "finalize", "agent", "pass completed", manifest);
            await repo.FinishSyncRunAsync(runId, "succeeded", null, rows, manifest);
        } catch (Exception ex) {
            await repo.LogEventAsync(runId, "error", "fatal", "agent", ex.Message, new { stack = ex.ToString() });
            await repo.FinishSyncRunAsync(runId, "failed", ex.Message, rows, null);
            await repo.OpenIncidentAsync("sync.failed", "critical", runId.ToString(), $"Sync run failed: {ex.Message}", new { runId, mode });
            throw;
        }
    }

    int processed = 0;
    while (true) {
        var claimed = await repo.ClaimNextPendingAsync();
        if (claimed == null) break;
        var (reqId, mode) = claimed.Value;
        Console.WriteLine($"[BlipSyncAgent] claimed sync_request id={reqId} mode={mode}");
        try {
            await RunOnePassAsync(mode, reqId);
            await repo.MarkCompletedAsync(reqId);
            processed++;
        } catch (Exception ex) {
            await repo.MarkCompletedAsync(reqId, error: ex.ToString());
        }
    }

    if (processed == 0) {
        // Heartbeat: every Actions invocation still does an incremental sync so cadence holds.
        await RunOnePassAsync("heartbeat", null);
    }

    Console.WriteLine($"[BlipSyncAgent] done. processed={processed}");
    return 0;
} catch (Exception ex) {
    Console.Error.WriteLine("[BlipSyncAgent] FATAL: " + ex);
    return 1;
}
