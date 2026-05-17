namespace BlipSyncAgent.Models;
public class Campaign {
    public string Id { get; set; } = "";
    public string? Name { get; set; }
    public string? Advertiser { get; set; }
    public string? Status { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public decimal? Budget { get; set; }
    public string? RawJson { get; set; }
}
