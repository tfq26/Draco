using System;

namespace Draco.Domain.Entities;

public class RemediationAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ResourceId { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty; // e.g., "ModifySecurityGroup", "StopInstance"
    public string Status { get; set; } = "Pending"; // Pending, InProgress, Succeeded, Failed
    
    public string? Description { get; set; }
    public string? AIReasoning { get; set; }
    
    public string? PreviousState { get; set; } // JSON snapshot before action
    public string? NewState { get; set; }      // JSON snapshot after action
    
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    
    public string? ErrorMessage { get; set; }
    public string? ExecutedBy { get; set; } // The ID of the user or "System"
}
