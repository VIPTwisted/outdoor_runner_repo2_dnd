namespace BlipSyncAgent.Models;
public class Ad {
    public string Id { get; set; } = "";
    public string? CampaignId { get; set; }
    public string? BoardId { get; set; }
    public string? Status { get; set; }
    public DateTime? LastServedAt { get; set; }
    public string? RawJson { get; set; }
}
