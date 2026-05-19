using OpenQA.Selenium;
using OpenQA.Selenium.Edge;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BlipSyncAgent.BlipClient;

public sealed class BlipNetworkEvent {
    public string Kind { get; set; } = "";
    public string Method { get; set; } = "";
    public string Url { get; set; } = "";
    public int? Status { get; set; }
    public string ContentType { get; set; } = "";
    public string BodyPreview { get; set; } = "";
    public long CapturedAtMs { get; set; }
}

public sealed class BlipNetworkRecorder {
    private static readonly Regex SafeFile = new(@"[^a-zA-Z0-9_.-]+", RegexOptions.Compiled);
    private readonly BlipSession _session;

    private const string RecorderScript = """
(function () {
  if (window.__blipRecorderInstalled) return;
  window.__blipRecorderInstalled = true;
  window.__blipNetworkLog = window.__blipNetworkLog || [];

  function preview(value) {
    if (value === undefined || value === null) return "";
    try {
      var text = typeof value === "string" ? value : JSON.stringify(value);
      return text.length > 20000 ? text.substring(0, 20000) : text;
    } catch (err) {
      return "";
    }
  }

  function push(evt) {
    try {
      var url = String(evt.url || "");
      if (!url || (!url.includes("blipbillboards.com") && !url.includes("/api/") && !url.includes("/gql"))) return;
      window.__blipNetworkLog.push({
        kind: evt.kind || "unknown",
        method: evt.method || "GET",
        url: url,
        status: evt.status || null,
        contentType: evt.contentType || "",
        bodyPreview: preview(evt.bodyPreview || ""),
        capturedAtMs: Date.now()
      });
    } catch (err) {
    }
  }

  var originalFetch = window.fetch;
  if (typeof originalFetch === "function") {
    window.fetch = async function(input, init) {
      var method = (init && init.method) || "GET";
      var url = typeof input === "string" ? input : (input && input.url) || "";
      try {
        var response = await originalFetch.apply(this, arguments);
        var clone = response.clone();
        clone.text().then(function(text) {
          push({
            kind: "fetch",
            method: method,
            url: url,
            status: response.status,
            contentType: response.headers ? (response.headers.get("content-type") || "") : "",
            bodyPreview: text
          });
        }).catch(function() {
          push({ kind: "fetch", method: method, url: url, status: response.status });
        });
        return response;
      } catch (err) {
        push({ kind: "fetch-error", method: method, url: url, bodyPreview: String(err && err.message || err) });
        throw err;
      }
    };
  }

  var originalOpen = XMLHttpRequest.prototype.open;
  var originalSend = XMLHttpRequest.prototype.send;
  XMLHttpRequest.prototype.open = function(method, url) {
    this.__blipRequestMeta = { method: method || "GET", url: url || "" };
    return originalOpen.apply(this, arguments);
  };
  XMLHttpRequest.prototype.send = function() {
    var xhr = this;
    xhr.addEventListener("loadend", function() {
      var meta = xhr.__blipRequestMeta || {};
      var body = "";
      try {
        if (typeof xhr.responseText === "string") body = xhr.responseText;
      } catch (err) {
      }
      push({
        kind: "xhr",
        method: meta.method || "GET",
        url: meta.url || "",
        status: xhr.status || null,
        contentType: xhr.getResponseHeader ? (xhr.getResponseHeader("content-type") || "") : "",
        bodyPreview: body
      });
    });
    return originalSend.apply(this, arguments);
  };
})();
""";

    public BlipNetworkRecorder(BlipSession session) {
        _session = session;
    }

    public void Install() {
        try {
            if (_session.Driver is EdgeDriver edge) {
                edge.ExecuteCdpCommand("Page.addScriptToEvaluateOnNewDocument", new Dictionary<string, object> {
                    ["source"] = RecorderScript
                });
            }
        } catch (Exception ex) {
            Console.WriteLine($"[network] cdp install warning: {ex.GetType().Name}: {ex.Message}");
        }

        try {
            ((IJavaScriptExecutor)_session.Driver).ExecuteScript(RecorderScript);
        } catch (Exception ex) {
            Console.WriteLine($"[network] runtime install warning: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public void Clear() {
        try {
            ((IJavaScriptExecutor)_session.Driver).ExecuteScript("window.__blipNetworkLog = [];");
        } catch {
        }
    }

    public IReadOnlyList<BlipNetworkEvent> Read() {
        try {
            var raw = ((IJavaScriptExecutor)_session.Driver).ExecuteScript("return window.__blipNetworkLog || [];");
            if (raw is not IEnumerable<object> items) return Array.Empty<BlipNetworkEvent>();

            return items
                .OfType<Dictionary<string, object>>()
                .Select(ToEvent)
                .Where(evt => !string.IsNullOrWhiteSpace(evt.Url))
                .GroupBy(evt => $"{evt.Kind}|{evt.Method}|{evt.Url}|{evt.Status}|{evt.BodyPreview}")
                .Select(group => group.First())
                .ToList();
        } catch (Exception ex) {
            Console.WriteLine($"[network] read failed: {ex.GetType().Name}: {ex.Message}");
            return Array.Empty<BlipNetworkEvent>();
        }
    }

    public void WriteDiagnostics(string sectionId, IReadOnlyList<BlipNetworkEvent> events) {
        try {
            Directory.CreateDirectory("blip-diagnostics");
            var safeSection = SafeFile.Replace(sectionId, "-").Trim('-');
            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
            var path = Path.Combine("blip-diagnostics", $"{stamp}-{safeSection}-network.json");
            File.WriteAllText(path, JsonSerializer.Serialize(events, new JsonSerializerOptions { WriteIndented = true }));

            var restCount = events.Count(evt => evt.Url.Contains("/api/", StringComparison.OrdinalIgnoreCase));
            var gqlCount = events.Count(evt => evt.Url.Contains("/gql", StringComparison.OrdinalIgnoreCase) || evt.Url.Contains("graphql", StringComparison.OrdinalIgnoreCase));
            Console.WriteLine($"[network] {sectionId} events={events.Count} rest={restCount} graphql={gqlCount} artifact={path}");
        } catch (Exception ex) {
            Console.WriteLine($"[network] diagnostics failed for {sectionId}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static BlipNetworkEvent ToEvent(Dictionary<string, object> row) {
        return new BlipNetworkEvent {
            Kind = GetString(row, "kind"),
            Method = GetString(row, "method"),
            Url = GetString(row, "url"),
            Status = GetInt(row, "status"),
            ContentType = GetString(row, "contentType"),
            BodyPreview = GetString(row, "bodyPreview"),
            CapturedAtMs = GetLong(row, "capturedAtMs")
        };
    }

    private static string GetString(Dictionary<string, object> row, string key) {
        return row.TryGetValue(key, out var value) ? value?.ToString() ?? "" : "";
    }

    private static int? GetInt(Dictionary<string, object> row, string key) {
        if (!row.TryGetValue(key, out var value) || value is null) return null;
        return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static long GetLong(Dictionary<string, object> row, string key) {
        if (!row.TryGetValue(key, out var value) || value is null) return 0;
        return long.TryParse(value.ToString(), out var parsed) ? parsed : 0;
    }
}
