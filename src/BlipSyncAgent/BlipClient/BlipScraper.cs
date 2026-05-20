using BlipSyncAgent.Models;
using OpenQA.Selenium;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BlipSyncAgent.BlipClient;

public sealed class BlipScraper {
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private readonly BlipSession _s;

    public BlipScraper(BlipSession s) { _s = s; }

    public List<ScrapedDashboardWidget> ScrapeDashboardWidgets() {
        return ExtractTableRows("dashboard")
            .Select(r => new ScrapedDashboardWidget {
                Id = r.Id,
                SectionId = r.SectionId,
                SourceUrl = r.SourceUrl,
                Org = At(r.Cells, 0),
                Sign = At(r.Cells, 1),
                SlotNumber = At(r.Cells, 2),
                AdGroup = At(r.Cells, 3),
                DaysLeft = At(r.Cells, 4),
                RawJson = r.RawJson
            })
            .Where(r => HasAny(r.Org, r.Sign, r.AdGroup))
            .ToList();
    }

    public List<ScrapedPlantSign> ScrapePlantSigns() {
        return ExtractTableRows("plant/signs")
            .Select(r => new ScrapedPlantSign {
                Id = r.Id,
                SectionId = r.SectionId,
                SourceUrl = r.SourceUrl,
                Title = At(r.Cells, 0),
                Subtitle = At(r.Cells, 1),
                RawText = r.RawText,
                RawJson = r.RawJson
            })
            .Where(r => HasAny(r.Title, r.Subtitle, r.RawText))
            .ToList();
    }

    public List<ScrapedAdkomAvailability> ScrapeAdkomAvailability() {
        return ExtractTableRows("adkom/availability")
            .Select(r => new ScrapedAdkomAvailability {
                Id = r.Id,
                SectionId = r.SectionId,
                SourceUrl = r.SourceUrl,
                Proposal = At(r.Cells, 0),
                Requested = At(r.Cells, 1),
                StartDate = At(r.Cells, 2),
                EndDate = At(r.Cells, 3),
                DueDate = At(r.Cells, 4),
                Units = At(r.Cells, 5),
                Status = At(r.Cells, 6),
                RawJson = r.RawJson
            })
            .Where(r => HasAny(r.Proposal, r.Requested, r.StartDate, r.EndDate))
            .ToList();
    }

    public List<ScrapedAdkomHold> ScrapeAdkomHolds() {
        return ExtractTableRows("adkom/hold")
            .Select(r => new ScrapedAdkomHold {
                Id = r.Id,
                SectionId = r.SectionId,
                SourceUrl = r.SourceUrl,
                Proposal = At(r.Cells, 0),
                Requested = At(r.Cells, 1),
                StartDate = At(r.Cells, 2),
                EndDate = At(r.Cells, 3),
                Units = At(r.Cells, 4),
                ProposedRate = At(r.Cells, 5),
                Status = At(r.Cells, 6),
                RawJson = r.RawJson
            })
            .Where(r => HasAny(r.Proposal, r.Requested, r.StartDate, r.EndDate))
            .ToList();
    }

    public List<ScrapedAdkomContract> ScrapeAdkomContracts() {
        return ExtractTableRows("adkom/contract")
            .Select(r => {
                var cells = r.Cells;
                var hasRequestedColumn = cells.Count >= 8;
                return new ScrapedAdkomContract {
                    Id = r.Id,
                    SectionId = r.SectionId,
                    SourceUrl = r.SourceUrl,
                    ContractNo = At(cells, 0),
                    Proposal = At(cells, 1),
                    StartDate = At(cells, hasRequestedColumn ? 3 : 2),
                    EndDate = At(cells, hasRequestedColumn ? 4 : 3),
                    Units = At(cells, hasRequestedColumn ? 5 : 4),
                    Amount = At(cells, hasRequestedColumn ? 6 : 5),
                    Status = At(cells, hasRequestedColumn ? 7 : 6),
                    RawJson = r.RawJson
                };
            })
            .Where(r => HasAny(r.ContractNo, r.Proposal, r.StartDate, r.EndDate))
            .ToList();
    }

    public List<ScrapedAdkomCreative> ScrapeAdkomCreatives() {
        return ExtractTableRows("adkom/creative")
            .Select(r => new ScrapedAdkomCreative {
                Id = r.Id,
                SectionId = r.SectionId,
                SourceUrl = r.SourceUrl,
                Proposal = At(r.Cells, 0),
                ReviewDate = At(r.Cells, 1),
                Creatives = At(r.Cells, 2),
                RawJson = r.RawJson
            })
            .Where(r => HasAny(r.Proposal, r.ReviewDate, r.Creatives))
            .ToList();
    }

    public List<ScrapedAdkomPop> ScrapeAdkomPop() {
        return ExtractTableRows("adkom/pop")
            .Select(r => new ScrapedAdkomPop {
                Id = r.Id,
                SectionId = r.SectionId,
                SourceUrl = r.SourceUrl,
                Proposal = At(r.Cells, 0),
                PopDate = At(r.Cells, 1),
                Creatives = At(r.Cells, 2),
                RawJson = r.RawJson
            })
            .Where(r => HasAny(r.Proposal, r.PopDate, r.Creatives))
            .ToList();
    }

    public List<ScrapedNamedRow> ScrapeMarketplaceGroups() {
        return ExtractTableRows("marketplace")
            .Select(r => new ScrapedNamedRow {
                Id = r.Id,
                SectionId = r.SectionId,
                SourceUrl = r.SourceUrl,
                Name = At(r.Cells, 0),
                Description = At(r.Cells, 1),
                RawJson = r.RawJson
            })
            .Where(r => HasAny(r.Name, r.Description))
            .ToList();
    }

    public List<ScrapedNamedRow> ScrapeProgrammaticReports() {
        return ExtractTableRows("programmatic/report")
            .Select(r => new ScrapedNamedRow {
                Id = r.Id,
                SectionId = r.SectionId,
                SourceUrl = r.SourceUrl,
                Name = At(r.Cells, 0),
                Description = At(r.Cells, 1),
                Created = At(r.Cells, 2),
                Status = At(r.Cells, 3),
                Format = At(r.Cells, 4),
                RawJson = r.RawJson
            })
            .Where(r => HasAny(r.Name, r.Description, r.Created, r.Status))
            .ToList();
    }

    public ScrapedPageSnapshot ScrapePageSnapshot(string sectionId) {
        WaitForPageToSettle();
        ScrollPage();
        ScrollScrollableContainers();

        var title = _s.Driver.Title ?? "";
        var sourceUrl = _s.Driver.Url;
        var bodyText = Clean(_s.Driver.FindElement(By.TagName("body")).Text);
        var links = ScrapeLinks(sectionId);
        var media = ScrapeMediaAssets(sectionId);
        var rawJson = JsonSerializer.Serialize(new {
            section_id = sectionId,
            source_url = sourceUrl,
            title,
            body_text = bodyText,
            links = links.Select(link => new { link.Href, link.Label, link.TargetSection }).ToArray(),
            media = media.Select(asset => new { asset.AssetUrl, asset.AssetType, asset.AltText, asset.Width, asset.Height }).ToArray()
        });

        return new ScrapedPageSnapshot {
            Id = StablePositiveId($"snapshot|{sectionId}|{sourceUrl}"),
            SectionId = sectionId,
            SourceUrl = sourceUrl,
            Title = EmptyToNull(title),
            BodyText = bodyText,
            LinkCount = links.Count,
            MediaCount = media.Count,
            RawJson = rawJson
        };
    }

    public List<ScrapedPageLink> ScrapeLinks(string sectionId) {
        var sourceUrl = _s.Driver.Url;
        return _s.Driver.FindElements(By.CssSelector("a[href], button, [role='button'], [data-testid], [aria-label]"))
            .Select(element => {
                var href = SafeAttr(element, "href") ?? "";
                var label = EmptyToNull(Clean(element.Text)) ?? EmptyToNull(SafeAttr(element, "aria-label")) ?? EmptyToNull(SafeAttr(element, "title"));
                var rawJson = JsonSerializer.Serialize(new {
                    section_id = sectionId,
                    source_url = sourceUrl,
                    href,
                    label,
                    tag = SafeAttr(element, "tagName"),
                    class_name = SafeAttr(element, "class"),
                    role = SafeAttr(element, "role"),
                    data_testid = SafeAttr(element, "data-testid"),
                    aria_label = SafeAttr(element, "aria-label")
                });

                return new ScrapedPageLink {
                    Id = StablePositiveId($"link|{sectionId}|{sourceUrl}|{href}|{label}|{rawJson}"),
                    SectionId = sectionId,
                    SourceUrl = sourceUrl,
                    Href = href,
                    Label = label,
                    TargetSection = InferTargetSection(href, label),
                    RawJson = rawJson
                };
            })
            .Where(link => !string.IsNullOrWhiteSpace(link.Href) || !string.IsNullOrWhiteSpace(link.Label))
            .GroupBy(link => link.Id)
            .Select(group => group.First())
            .ToList();
    }

    public List<ScrapedMediaAsset> ScrapeMediaAssets(string sectionId) {
        var sourceUrl = _s.Driver.Url;
        return _s.Driver.FindElements(By.CssSelector("img[src], video[src], source[src], canvas, [style*='background-image']"))
            .Select(element => {
                var assetUrl = SafeAttr(element, "src") ?? ExtractBackgroundImageUrl(SafeAttr(element, "style")) ?? "";
                var tag = (SafeAttr(element, "tagName") ?? "").ToLowerInvariant();
                var width = ParseNullableInt(SafeAttr(element, "naturalWidth")) ?? ParseNullableInt(SafeAttr(element, "width"));
                var height = ParseNullableInt(SafeAttr(element, "naturalHeight")) ?? ParseNullableInt(SafeAttr(element, "height"));
                var screenshotBase64 = CaptureElementScreenshot(element);
                var rawJson = JsonSerializer.Serialize(new {
                    section_id = sectionId,
                    source_url = sourceUrl,
                    asset_url = assetUrl,
                    tag,
                    alt = SafeAttr(element, "alt"),
                    title = SafeAttr(element, "title"),
                    class_name = SafeAttr(element, "class"),
                    width,
                    height,
                    screenshot_base64 = screenshotBase64,
                    outer_html = SafeAttr(element, "outerHTML")
                });

                return new ScrapedMediaAsset {
                    Id = StablePositiveId($"media|{sectionId}|{sourceUrl}|{assetUrl}|{rawJson}"),
                    SectionId = sectionId,
                    SourceUrl = sourceUrl,
                    AssetUrl = assetUrl,
                    AssetType = string.IsNullOrWhiteSpace(tag) ? "unknown" : tag,
                    AltText = EmptyToNull(SafeAttr(element, "alt")) ?? EmptyToNull(SafeAttr(element, "title")),
                    Width = width,
                    Height = height,
                    RawJson = rawJson
                };
            })
            .Where(asset => !string.IsNullOrWhiteSpace(asset.AssetUrl) || asset.AssetType == "canvas")
            .GroupBy(asset => asset.Id)
            .Select(group => group.First())
            .ToList();
    }

    public void WriteDiagnostics(string sectionId) {
        try {
            Directory.CreateDirectory("blip-diagnostics");
            var safeSection = Regex.Replace(sectionId, @"[^a-zA-Z0-9_.-]+", "-").Trim('-');
            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
            var prefix = Path.Combine("blip-diagnostics", $"{stamp}-{safeSection}");
            var title = _s.Driver.Title ?? "";
            var url = _s.Driver.Url ?? "";
            var text = Clean(_s.Driver.FindElement(By.TagName("body")).Text);
            var html = _s.Driver.PageSource ?? "";
            var tableCount = _s.Driver.FindElements(By.CssSelector("table")).Count;
            var trCount = _s.Driver.FindElements(By.CssSelector("tr")).Count;
            var roleRowCount = _s.Driver.FindElements(By.CssSelector("[role='row']")).Count;
            var matRowCount = _s.Driver.FindElements(By.CssSelector(".mat-row, .mat-mdc-row")).Count;

            File.WriteAllText(prefix + ".txt", string.Join(Environment.NewLine, new[] {
                $"section={sectionId}",
                $"url={url}",
                $"title={title}",
                $"body_text_length={text.Length}",
                $"html_length={html.Length}",
                $"table_count={tableCount}",
                $"tr_count={trCount}",
                $"role_row_count={roleRowCount}",
                $"mat_row_count={matRowCount}",
                "",
                text
            }));
            File.WriteAllText(prefix + ".html", html);

            if (_s.Driver is ITakesScreenshot shooter) {
                shooter.GetScreenshot().SaveAsFile(prefix + ".png");
            }

            Console.WriteLine($"[scraper] diagnostics written for {sectionId}: url={url} title={title} textLength={text.Length} htmlLength={html.Length} tables={tableCount} trs={trCount} roleRows={roleRowCount} matRows={matRowCount}");
        } catch (Exception ex) {
            Console.WriteLine($"[scraper] diagnostics failed for {sectionId}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private List<ScrapedTableRow> ExtractTableRows(string sectionId) {
        WaitForPageToSettle();
        ScrollPage();
        ScrollScrollableContainers();

        var rows = _s.Driver
            .FindElements(By.CssSelector(string.Join(", ", new[] {
                "table tbody tr",
                "tr.mat-row",
                "tr.mat-mdc-row",
                "tr[role='row']",
                ".mat-row",
                ".mat-mdc-row",
                ".MuiTableRow-root",
                ".MuiDataGrid-row",
                "cdk-row",
                "mat-row",
                "[role='row']",
                "[role='listitem']",
                ".mat-list-item",
                ".mat-mdc-list-item",
                ".mat-card",
                ".mat-mdc-card",
                ".MuiCard-root",
                ".card",
                "li"
            })))
            .Select(row => SnapshotRow(sectionId, row))
            .Where(row => row.Cells.Count > 0 && !LooksLikeHeader(row.Cells) && !LooksLikeChrome(row.RawText))
            .GroupBy(row => row.Id)
            .Select(group => group.First())
            .ToList();

        Console.WriteLine($"[scraper] {sectionId} rows={rows.Count}");
        return rows;
    }

    private ScrapedTableRow SnapshotRow(string sectionId, IWebElement row) {
        var rawText = Clean(row.Text);
        var rawHtml = SafeAttr(row, "outerHTML") ?? "";
        var cells = row
            .FindElements(By.CssSelector("td, th, [role='cell'], .mat-cell, .mat-mdc-cell, .MuiTableCell-root"))
            .Select(cell => Clean(cell.Text))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        if (cells.Count == 0 && !string.IsNullOrWhiteSpace(rawText)) {
            cells = rawText
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Clean)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToList();
        }

        var sourceUrl = _s.Driver.Url;
        var rawJson = JsonSerializer.Serialize(new {
            section_id = sectionId,
            source_url = sourceUrl,
            cells,
            raw_text = rawText,
            html = rawHtml,
            element_tag = SafeAttr(row, "tagName"),
            class_name = SafeAttr(row, "class"),
            role = SafeAttr(row, "role"),
            data_testid = SafeAttr(row, "data-testid"),
            aria_label = SafeAttr(row, "aria-label")
        });

        return new ScrapedTableRow {
            Id = StablePositiveId($"{sectionId}|{sourceUrl}|{rawText}|{rawHtml}"),
            SectionId = sectionId,
            SourceUrl = sourceUrl,
            RawText = rawText,
            RawJson = rawJson,
            Cells = cells
        };
    }

    private void WaitForPageToSettle() {
        Thread.Sleep(2500);
        try {
            var js = (IJavaScriptExecutor)_s.Driver;
            for (var i = 0; i < 20; i++) {
                var ready = js.ExecuteScript("return document.readyState")?.ToString();
                if (string.Equals(ready, "complete", StringComparison.OrdinalIgnoreCase)) break;
                Thread.Sleep(250);
            }
        } catch {
            Thread.Sleep(1000);
        }
    }

    private void ScrollPage() {
        try {
            var js = (IJavaScriptExecutor)_s.Driver;
            var lastHeight = Convert.ToInt64(js.ExecuteScript("return Math.max(document.body.scrollHeight, document.documentElement.scrollHeight)") ?? 0L);
            for (var i = 0; i < 8; i++) {
                js.ExecuteScript("window.scrollTo(0, document.body.scrollHeight)");
                Thread.Sleep(400);
                var nextHeight = Convert.ToInt64(js.ExecuteScript("return Math.max(document.body.scrollHeight, document.documentElement.scrollHeight)") ?? 0L);
                if (nextHeight <= lastHeight) break;
                lastHeight = nextHeight;
            }
            js.ExecuteScript("window.scrollTo(0, 0)");
        } catch {
            Thread.Sleep(500);
        }
    }

    private void ScrollScrollableContainers() {
        try {
            var js = (IJavaScriptExecutor)_s.Driver;
            js.ExecuteScript("""
const nodes = Array.from(document.querySelectorAll('*'))
  .filter(el => {
    const style = window.getComputedStyle(el);
    return el.scrollHeight > el.clientHeight + 40 && ['auto', 'scroll'].includes(style.overflowY);
  })
  .slice(0, 20);
for (const node of nodes) {
  node.scrollTop = 0;
  for (let i = 0; i < 8; i++) node.scrollTop = node.scrollHeight;
}
""");
            Thread.Sleep(750);
        } catch {
            Thread.Sleep(250);
        }
    }

    private static bool LooksLikeHeader(IReadOnlyList<string> cells) {
        if (cells.Count == 0) return true;
        var joined = string.Join("|", cells).ToLowerInvariant();
        return joined is "proposal|requested|start|end|due|# units" or "contract no|proposal|requested|start|end|# units|amount|status";
    }

    private static bool LooksLikeChrome(string rawText) {
        if (string.IsNullOrWhiteSpace(rawText)) return true;
        var normalized = Clean(rawText).ToLowerInvariant();
        if (normalized.Length < 3) return true;
        if (normalized.Length > 4000) return true;
        return normalized is "dashboard" or "fold all" or "help" or "revenue sources" or "adkom" or "marketplace" or "programmatic" or "plant management" or "organization";
    }

    private static string? At(IReadOnlyList<string> cells, int index) {
        return index >= 0 && index < cells.Count ? EmptyToNull(cells[index]) : null;
    }

    private static string? EmptyToNull(string? value) {
        var cleaned = Clean(value);
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    private static string Clean(string? value) {
        return string.IsNullOrWhiteSpace(value) ? "" : Whitespace.Replace(value.Trim(), " ");
    }

    private static string? SafeAttr(IWebElement element, string attr) {
        try { return element.GetAttribute(attr); } catch { return null; }
    }

    private static bool HasAny(params string?[] values) {
        return values.Any(value => !string.IsNullOrWhiteSpace(value));
    }

    private static int? ParseNullableInt(string? value) {
        return int.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string? ExtractBackgroundImageUrl(string? style) {
        if (string.IsNullOrWhiteSpace(style)) return null;
        var match = Regex.Match(style, @"url\([""']?(?<url>[^)""']+)[""']?\)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["url"].Value : null;
    }

    private static string? CaptureElementScreenshot(IWebElement element) {
        try {
            if (element is not ITakesScreenshot shooter) return null;
            var screenshot = shooter.GetScreenshot();
            var encoded = screenshot.AsBase64EncodedString;
            return encoded.Length > 750000 ? null : encoded;
        } catch {
            return null;
        }
    }

    private static string? InferTargetSection(string? href, string? label) {
        var haystack = $"{href} {label}".ToLowerInvariant();
        if (haystack.Contains("available")) return "adkom-availability";
        if (haystack.Contains("hold")) return "adkom-holds";
        if (haystack.Contains("contract")) return "adkom-contracts";
        if (haystack.Contains("creative")) return "adkom-creatives";
        if (haystack.Contains("pop")) return "adkom-pop";
        if (haystack.Contains("marketplace")) return "marketplace";
        if (haystack.Contains("programmatic")) return "programmatic";
        if (haystack.Contains("sign")) return "plant-signs";
        if (haystack.Contains("campaign")) return "campaigns";
        if (haystack.Contains("moderation")) return "ad-moderation";
        return null;
    }

    private static long StablePositiveId(string input) {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var value = BitConverter.ToInt64(bytes, 0) & long.MaxValue;
        return value == 0 ? 1 : value;
    }
}
