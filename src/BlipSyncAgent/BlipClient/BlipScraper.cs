using BlipSyncAgent.Models;
using OpenQA.Selenium;
using System.Text.Json;

namespace BlipSyncAgent.BlipClient;

public class BlipScraper {
    private readonly BlipSession _s;
    public BlipScraper(BlipSession s) { _s = s; }

    public List<Board> ScrapeBoards() {
        var rows = new List<Board>();
        // BLIP renders inventory rows with a stable data-attribute. Adjust the selector
        // to whatever your UI actually exposes — the catch-all extractor in the main repo
        // gives you raw HTML if selectors miss.
        foreach (var tr in _s.Driver.FindElements(By.CssSelector("tr[data-sign-id], [data-board-id]"))) {
            var id = tr.GetAttribute("data-sign-id") ?? tr.GetAttribute("data-board-id");
            if (string.IsNullOrEmpty(id)) continue;
            rows.Add(new Board {
                Id       = id,
                Name     = SafeText(tr, "[data-col='name']"),
                Location = SafeText(tr, "[data-col='address']") ?? SafeText(tr, "[data-col='city']"),
                Status   = SafeText(tr, "[data-col='status']"),
                RawJson  = JsonSerializer.Serialize(new { html = tr.GetAttribute("outerHTML") })
            });
        }
        Console.WriteLine($"[scraper] boards={rows.Count}");
        return rows;
    }

    public List<Campaign> ScrapeCampaigns() {
        var rows = new List<Campaign>();
        foreach (var tr in _s.Driver.FindElements(By.CssSelector("tr[data-campaign-id]"))) {
            var id = tr.GetAttribute("data-campaign-id");
            if (string.IsNullOrEmpty(id)) continue;
            rows.Add(new Campaign {
                Id         = id,
                Name       = SafeText(tr, "[data-col='name']"),
                Advertiser = SafeText(tr, "[data-col='advertiser']"),
                Status     = SafeText(tr, "[data-col='status']"),
                RawJson    = JsonSerializer.Serialize(new { html = tr.GetAttribute("outerHTML") })
            });
        }
        Console.WriteLine($"[scraper] campaigns={rows.Count}");
        return rows;
    }

    public List<Ad> ScrapeAds() {
        var rows = new List<Ad>();
        foreach (var tr in _s.Driver.FindElements(By.CssSelector("tr[data-ad-id]"))) {
            var id = tr.GetAttribute("data-ad-id");
            if (string.IsNullOrEmpty(id)) continue;
            rows.Add(new Ad {
                Id         = id,
                CampaignId = SafeAttr(tr, "[data-col='campaign']", "data-id"),
                BoardId    = SafeAttr(tr, "[data-col='board']",    "data-id"),
                Status     = SafeText(tr, "[data-col='status']"),
                RawJson    = JsonSerializer.Serialize(new { html = tr.GetAttribute("outerHTML") })
            });
        }
        Console.WriteLine($"[scraper] ads={rows.Count}");
        return rows;
    }

    private static string? SafeText(IWebElement scope, string css) {
        try { return scope.FindElement(By.CssSelector(css)).Text?.Trim(); } catch { return null; }
    }
    private static string? SafeAttr(IWebElement scope, string css, string attr) {
        try { return scope.FindElement(By.CssSelector(css)).GetAttribute(attr); } catch { return null; }
    }
}
