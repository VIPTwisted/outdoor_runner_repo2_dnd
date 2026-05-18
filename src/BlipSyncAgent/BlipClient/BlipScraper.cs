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

    private List<ScrapedTableRow> ExtractTableRows(string sectionId) {
        WaitForPageToSettle();
        ScrollPage();

        var rows = _s.Driver
            .FindElements(By.CssSelector("table tbody tr, tr.mat-row, tr.mat-mdc-row, tr[role='row'], .mat-row, .mat-mdc-row, .MuiTableRow-root"))
            .Select(row => SnapshotRow(sectionId, row))
            .Where(row => row.Cells.Count > 0 && !LooksLikeHeader(row.Cells))
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
            html = rawHtml
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

    private static bool LooksLikeHeader(IReadOnlyList<string> cells) {
        if (cells.Count == 0) return true;
        var joined = string.Join("|", cells).ToLowerInvariant();
        return joined is "proposal|requested|start|end|due|# units" or "contract no|proposal|requested|start|end|# units|amount|status";
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

    private static long StablePositiveId(string input) {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var value = BitConverter.ToInt64(bytes, 0) & long.MaxValue;
        return value == 0 ? 1 : value;
    }
}
