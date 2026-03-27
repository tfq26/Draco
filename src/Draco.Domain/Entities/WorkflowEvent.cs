namespace Draco.Domain.Entities;

public class WorkflowEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public UserAccount? User { get; set; }
    public string Source { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Severity { get; set; } = "Medium";
    public string Provider { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
    public string? ResourceId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }
    public int AttemptCount { get; set; }
    public string? CorrelationId { get; set; }
    public string? RawPayload { get; set; }
    public string? ProcessingError { get; set; }
}

public class WorkflowRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public UserAccount? User { get; set; }
    public Guid? WorkflowEventId { get; set; }
    public WorkflowEvent? WorkflowEvent { get; set; }
    public string WorkflowType { get; set; } = string.Empty;
    public string Trigger { get; set; } = string.Empty;
    public string SuggestedAction { get; set; } = string.Empty;
    public string Severity { get; set; } = "Medium";
    public string Provider { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
    public string? ResourceId { get; set; }
    public string Status { get; set; } = "Open";
    public bool CanAutoRun { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Recommendation { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
