using BlipSyncAgent;
using BlipSyncAgent.BlipClient;
using BlipSyncAgent.Data;
using Npgsql;
using System.Security.Cryptography;
using System.Text;

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
    var postgresConnectionStrings = BuildNpgsqlConnectionStrings(rawPostgresConnectionString);
    Console.WriteLine($"[BlipSyncAgent] POSTGRES_CONNECTION_STRING loaded prefix={maskedConnectionString} length={rawPostgresConnectionString.Length}");
    Console.WriteLine($"[BlipSyncAgent] POSTGRES_CONNECTION_STRING fingerprint={GetSecretFingerprint(rawPostgresConnectionString)}");
    Console.WriteLine($"[BlipSyncAgent] POSTGRES_CONNECTION_STRING normalized format={GetConnectionStringFormat(rawPostgresConnectionString)} candidates={postgresConnectionStrings.Count}");
    Console.WriteLine($"[BlipSyncAgent] start  slug={cfg.BlipOperatorSlug}  runner={runnerKind}  wfRun={workflowRunId}");

    SupabaseRepository repo;
    try {
        repo = OpenSupabaseRepository(postgresConnectionStrings);
    } catch (Exception dbEx) {
        Console.Error.WriteLine("[BlipSyncAgent] PostgreSQL persistence unavailable before scrape: " + dbEx.GetType().Name + ": " + dbEx.Message);
        Console.Error.WriteLine("[BlipSyncAgent] continuing in artifact-only forensic scrape mode; no database writes will be attempted.");

        var artifactRunId = Guid.NewGuid();
        var artifactSink = new ArtifactSyncSink();
        await artifactSink.LogEventAsync(artifactRunId, "error", "startup", "database", "PostgreSQL persistence unavailable before scrape", new {
            error_type = dbEx.GetType().FullName,
            message = dbEx.Message
        });

        using var blip = new BlipSession(cfg);
        await artifactSink.LogEventAsync(artifactRunId, "info", "login", "agent", "browser launched");
        await blip.LoginAsync();
        await artifactSink.LogEventAsync(artifactRunId, "info", "login", "agent", "login complete");
        var artifactManifest = await new SyncProcessor(artifactSink).RunWithForensicsAsync(blip, "artifact-only-db-unavailable", artifactRunId);
        await artifactSink.LogEventAsync(artifactRunId, "error", "finalize", "agent", "artifact-only scrape completed but database persistence is unavailable", artifactManifest);
        Console.Error.WriteLine($"[BlipSyncAgent] artifact-only scrape completed rows={artifactManifest.RowsUpserted}; failing workflow because database persistence is still unavailable.");
        return 2;
    }

    using (repo) {

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
    }
} catch (Exception ex) {
    Console.Error.WriteLine("[BlipSyncAgent] FATAL: " + ex);
    return 1;
}

static SupabaseRepository OpenSupabaseRepository(IReadOnlyList<string> connectionStrings) {
    Exception? lastException = null;

    for (var index = 0; index < connectionStrings.Count; index++) {
        var connectionString = connectionStrings[index];
        var diagnostics = GetSafeConnectionDiagnostics(connectionString);
        Console.WriteLine($"[BlipSyncAgent] POSTGRES_CONNECTION_STRING target[{index + 1}/{connectionStrings.Count}] host={diagnostics.Host} port={diagnostics.Port} database={diagnostics.Database} username={diagnostics.Username} ssl={diagnostics.SslMode}");

        try {
            var repo = new SupabaseRepository(connectionString);
            Console.WriteLine($"[BlipSyncAgent] PostgreSQL connection opened using target[{index + 1}/{connectionStrings.Count}].");
            return repo;
        } catch (Exception ex) when (index + 1 < connectionStrings.Count && IsRetryableStartupConnectionFailure(ex)) {
            lastException = ex;
            Console.WriteLine($"[BlipSyncAgent] PostgreSQL target[{index + 1}/{connectionStrings.Count}] failed during startup: {ex.GetType().Name}: {ex.Message}");
        }
    }

    throw lastException ?? new InvalidOperationException("No PostgreSQL connection candidates were available.");
}

static bool IsRetryableStartupConnectionFailure(Exception ex) {
    if (ex is NpgsqlException) return true;
    if (ex.InnerException is NpgsqlException) return true;
    return false;
}

static IReadOnlyList<string> BuildNpgsqlConnectionStrings(string rawConnectionString) {
    var trimmed = rawConnectionString.Trim();
    if (trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) ||
        trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)) {
        return ConvertPostgresUriToNpgsqlConnectionStrings(trimmed);
    }

    try {
        _ = new NpgsqlConnectionStringBuilder(trimmed);
        return new[] { trimmed };
    } catch (Exception ex) {
        throw new InvalidOperationException("POSTGRES_CONNECTION_STRING was loaded, but it is not a valid Npgsql key/value connection string or postgresql:// URI.", ex);
    }
}

static IReadOnlyList<string> ConvertPostgresUriToNpgsqlConnectionStrings(string postgresUri) {
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

    var host = uri.Host;
    var port = uri.IsDefaultPort ? 5432 : uri.Port;
    var database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
    var candidates = BuildSupabaseEndpointCandidates(host, port, username);

    var connectionStrings = new List<string>();
    foreach (var candidate in candidates) {
        var builder = new NpgsqlConnectionStringBuilder {
            Host = candidate.Host,
            Port = candidate.Port,
            Database = string.IsNullOrWhiteSpace(database) ? "postgres" : database,
            Username = candidate.Username,
            Password = password,
            SslMode = SslMode.Require
        };

        ApplyUriQueryOptions(uri.Query, builder);
        connectionStrings.Add(builder.ConnectionString);
    }

    return connectionStrings;
}

static IReadOnlyList<SupabaseEndpointCandidate> BuildSupabaseEndpointCandidates(string host, int port, string username) {
    const string directHostPrefix = "db.";
    const string directHostSuffix = ".supabase.co";

    if (port != 6543 ||
        !host.StartsWith(directHostPrefix, StringComparison.OrdinalIgnoreCase) ||
        !host.EndsWith(directHostSuffix, StringComparison.OrdinalIgnoreCase)) {
        return new[] { new SupabaseEndpointCandidate(host, port, username) };
    }

    var projectRefStart = directHostPrefix.Length;
    var projectRefLength = host.Length - directHostPrefix.Length - directHostSuffix.Length;
    if (projectRefLength <= 0) {
        throw new InvalidOperationException("POSTGRES_CONNECTION_STRING uses Supabase pooler port 6543 but the project ref could not be parsed from the db.<project-ref>.supabase.co host.");
    }

    var projectRef = host.Substring(projectRefStart, projectRefLength);
    var poolerUsername = username.Equals("postgres", StringComparison.OrdinalIgnoreCase)
        ? $"postgres.{projectRef}"
        : username;

    Console.WriteLine("[BlipSyncAgent] POSTGRES_CONNECTION_STRING detected Supabase pooler port with direct db host; normalized to pooler endpoint candidates.");
    return new[] {
        new SupabaseEndpointCandidate("aws-0-us-east-1.pooler.supabase.com", 6543, poolerUsername),
        new SupabaseEndpointCandidate("aws-1-us-east-1.pooler.supabase.com", 6543, poolerUsername)
    };
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
        string.IsNullOrWhiteSpace(builder.Username) ? "<missing>" : builder.Username,
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

static string GetSecretFingerprint(string value) {
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
    return Convert.ToHexString(hash).Substring(0, 16).ToLowerInvariant();
}

readonly record struct SupabaseEndpointCandidate(string Host, int Port, string Username);
readonly record struct SafeConnectionDiagnostics(string Host, int Port, string Database, string Username, string SslMode);
