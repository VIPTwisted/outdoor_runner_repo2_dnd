namespace BlipSyncAgent.Models;

public sealed class ScrapedTableRow {
    public long Id { get; set; }
    public string SectionId { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string RawText { get; set; } = "";
    public string RawJson { get; set; } = "{}";
    public IReadOnlyList<string> Cells { get; set; } = Array.Empty<string>();
}

public sealed class ScrapedPlantSign {
    public long Id { get; set; }
    public string SectionId { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public string RawText { get; set; } = "";
    public string RawJson { get; set; } = "{}";
}

public sealed class ScrapedAdkomAvailability {
    public long Id { get; set; }
    public string SectionId { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string? Proposal { get; set; }
    public string? Requested { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public string? DueDate { get; set; }
    public string? Units { get; set; }
    public string? Status { get; set; }
    public string RawJson { get; set; } = "{}";
}

public sealed class ScrapedAdkomHold {
    public long Id { get; set; }
    public string SectionId { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string? Proposal { get; set; }
    public string? Requested { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public string? Units { get; set; }
    public string? ProposedRate { get; set; }
    public string? Status { get; set; }
    public string RawJson { get; set; } = "{}";
}

public sealed class ScrapedAdkomContract {
    public long Id { get; set; }
    public string SectionId { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string? ContractNo { get; set; }
    public string? Proposal { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public string? Amount { get; set; }
    public string? Status { get; set; }
    public string? Units { get; set; }
    public string RawJson { get; set; } = "{}";
}

public sealed class ScrapedAdkomCreative {
    public long Id { get; set; }
    public string SectionId { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string? Proposal { get; set; }
    public string? ReviewDate { get; set; }
    public string? Creatives { get; set; }
    public string RawJson { get; set; } = "{}";
}

public sealed class ScrapedAdkomPop {
    public long Id { get; set; }
    public string SectionId { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string? Proposal { get; set; }
    public string? PopDate { get; set; }
    public string? Creatives { get; set; }
    public string RawJson { get; set; } = "{}";
}

public sealed class ScrapedDashboardWidget {
    public long Id { get; set; }
    public string SectionId { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string? Org { get; set; }
    public string? Sign { get; set; }
    public string? SlotNumber { get; set; }
    public string? AdGroup { get; set; }
    public string? DaysLeft { get; set; }
    public string RawJson { get; set; } = "{}";
}

public sealed class ScrapedNamedRow {
    public long Id { get; set; }
    public string SectionId { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Created { get; set; }
    public string? Status { get; set; }
    public string? Format { get; set; }
    public string RawJson { get; set; } = "{}";
}
