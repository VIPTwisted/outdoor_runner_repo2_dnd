namespace BlipSyncAgent.Models;
public class SyncRequest {
    public Guid Id { get; set; }
    public string Mode { get; set; } = "incremental"; // incremental | full
    public string Status { get; set; } = "pending";
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
