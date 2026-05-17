using BlipSyncAgent;
using BlipSyncAgent.BlipClient;
using BlipSyncAgent.Data;
using Npgsql;

try {
    var cfg = new Config();
    var workflowRunId = Environment.GetEnvironmentVariable("GITHUB_RUN_ID");
    var runnerKind    = string.IsNullOrEmpty(workflowRunId) ? "local" : "github-actions";

    var rawPostgresConnectionString = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING");
    if (string.IsNullOrWhiteSpace(rawPostgresConnectionString)) {
        throw new InvalidOperationException("POSTGRES_CONNECTION_STRING environment variable is required for BlipSyncAgent startup but was not set or was empty.");
    }

    var visiblePrefixLength = Math.Min(10, rawPostgresConnectionString.Length);
    var maskedConnectionString = rawPostgresConnectionString.Substring(0, visiblePrefixLength) + "...***";
    var postgresConnectionString = BuildNpgsqlConnectionString(rawPostgresConnectionString);
    var postgresConnectionDiagnostics = GetSafeConnectionDiagnostics(postgresConnectionString);
    Console.WriteLine($"[BlipSyncAgent] POSTGRES_CONNECTION_STRING loaded prefix={maskedConnectionString} length={rawPostgresConnectionString.Length}");
    Console.WriteLine($"[BlipSyncAgent] POSTGRES_CONNECTION_STRING normalized format={GetConnectionStringFormat(rawPostgresConnectionString)}");
    Console.WriteLine($"[BlipSyncAgent] POSTGRES_CONNECTION_STRING target host={postgresConnectionDiagnostics.Host} port={postgresConnectionDiagnostics.Port} database={postgresConnectionDiagnostics.Database} ssl={postgresConnectionDiagnostics.SslMode}");
    Console.WriteLine($"[BlipSyncAgent] start  slug={cfg.BlipOperatorSlug}  runner={runnerKind}  wfRun={workflowRunId}");

    using var repo = new SupabaseRepository(postgresConnectionString);

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

static string BuildNpgsqlConnectionString(string rawConnectionString) {
    var trimmed = rawConnectionString.Trim();
    if (trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) ||
        trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)) {
        return ConvertPostgresUriToNpgsqlConnectionString(trimmed);
    }

    try {
        _ = new NpgsqlConnectionStringBuilder(trimmed);
        return trimmed;
    } catch (Exception ex) {
        throw new InvalidOperationException("POSTGRES_CONNECTION_STRING was loaded, but it is not a valid Npgsql key/value connection string or postgresql:// URI.", ex);
    }
}

static string ConvertPostgresUriToNpgsqlConnectionString(string postgresUri) {
    if (!Uri.TryCreate(postgresUri, UriKind.Absolute, out var uri)) {
        throw new InvalidOperationException("POSTGRES_CONNECTION_STRING starts with postgres/postgresql but is not a valid absolute URI.");
    }

    if (string.IsNullOrWhiteSpace(uri.Host)) {
        throw new InvalidOperationException("POSTGRES_CONNECTION_STRING URI is missing a host.");
    }

    var userInfoParts = uri.UserInfo.Split(':', 2);
    var username = userInfoParts.Length > 0 ? Uri.UnescapeDataString(userInfoParts[0]) : string.Empty;
    var password = userInfoParts.Length > 1 ? Uri.UnescapeDataString(userInfoParts[1]) : string.Empty;
    if (string.IsNullOrWhiteSpace(username)) {
        throw new InvalidOperationException("POSTGRES_CONNECTION_STRING URI is missing a username.");
    }

    var database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
    var builder = new NpgsqlConnectionStringBuilder {
        Host = uri.Host,
        Port = uri.IsDefaultPort ? 5432 : uri.Port,
        Database = string.IsNullOrWhiteSpace(database) ? "postgres" : database,
        Username = username,
        Password = password,
        SslMode = SslMode.Require,
        TrustServerCertificate = true
    };

    ApplyUriQueryOptions(uri.Query, builder);
    return builder.ConnectionString;
}

static void ApplyUriQueryOptions(string query, NpgsqlConnectionStringBuilder builder) {
    if (string.IsNullOrWhiteSpace(query)) return;

    foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)) {
        var parts = pair.Split('=', 2);
        var key = Uri.UnescapeDataString(parts[0]).Trim();
        var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]).Trim() : string.Empty;

        if (key.Equals("sslmode", StringComparison.OrdinalIgnoreCase) &&
            Enum.TryParse<SslMode>(value, ignoreCase: true, out var sslMode)) {
            builder.SslMode = sslMode;
        } else if (key.Equals("application_name", StringComparison.OrdinalIgnoreCase)) {
            builder.ApplicationName = value;
        } else if (key.Equals("pooling", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out var pooling)) {
            builder.Pooling = pooling;
        } else if (key.Equals("timeout", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var timeout)) {
            builder.Timeout = timeout;
        } else if ((key.Equals("command_timeout", StringComparison.OrdinalIgnoreCase) || key.Equals("commandtimeout", StringComparison.OrdinalIgnoreCase)) && int.TryParse(value, out var commandTimeout)) {
            builder.CommandTimeout = commandTimeout;
        }
    }
}

static SafeConnectionDiagnostics GetSafeConnectionDiagnostics(string connectionString) {
    var builder = new NpgsqlConnectionStringBuilder(connectionString);
    return new SafeConnectionDiagnostics(
        string.IsNullOrWhiteSpace(builder.Host) ? "<missing>" : builder.Host,
        builder.Port,
        string.IsNullOrWhiteSpace(builder.Database) ? "<missing>" : builder.Database,
        builder.SslMode.ToString()
    );
}

static string GetConnectionStringFormat(string rawConnectionString) {
    var trimmed = rawConnectionString.Trim();
    if (trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) ||
        trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)) {
        return "postgres-uri-to-npgsql";
    }

    return "npgsql-key-value";
}

readonly record struct SafeConnectionDiagnostics(string Host, int Port, string Database, string SslMode);
