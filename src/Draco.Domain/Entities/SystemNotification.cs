using System;

namespace Draco.Domain.Entities;

public class SystemNotification
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public string NotificationKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = "Info"; // Info, Warning, Error, Success
    public string Severity { get; set; } = "Info";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastEvaluatedAt { get; set; }
    public DateTime? LastDeliveredAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public bool IsRead { get; set; }
    public string? ResourceUrl { get; set; }
    public string? Category { get; set; } // Compliance, Security, Cost, Inventory
    public string? Provider { get; set; }
    public string? SubscriptionId { get; set; }
    public string? ResourceId { get; set; }
    public string? Service { get; set; }
    public string? SourceRule { get; set; }
    public string? Metadata { get; set; }

    public virtual UserAccount? User { get; set; }
}
