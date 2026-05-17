namespace BlipSyncAgent.Models;
public class Board {
    public string Id { get; set; } = "";
    public string? Name { get; set; }
    public string? Location { get; set; }
    public string? Status { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public string? RawJson { get; set; }
}
