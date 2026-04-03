using System.ComponentModel.DataAnnotations;

namespace Draco.Domain.Entities;

public class UserAccount
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? Phone { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? ImageUrl { get; set; }
    public string? PreferredChannel { get; set; } = "SMS"; // Comma-separated: "SMS", "WhatsApp", or both
    public string? SmsRecipientsJson { get; set; }
    public string? WhatsAppRecipientsJson { get; set; }
    public string? AuthId { get; set; }
    public string? PasswordHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation property for connections
    public List<CloudConnection> Connections { get; set; } = new();
    public List<PulseReportSchedule> ReportSchedules { get; set; } = new();
}

public class CloudConnection
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public UserAccount? User { get; set; }
    public string Provider { get; set; } = string.Empty; // "Azure", "AWS"
    public string SubscriptionId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? AuthType { get; set; }
    public string? ExternalAccountId { get; set; }
    public string? AwsRoleArn { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTimeOffset? TokenExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset ConnectedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSyncedAt { get; set; }
    public string SyncStatus { get; set; } = "Pending";
    public string? SyncMessage { get; set; }
}

public class PulseReportSchedule
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public UserAccount? User { get; set; }
    public string Frequency { get; set; } = "Weekly"; // Daily, Weekly, Monthly
    public bool IncludeCostAnalysis { get; set; } = true;
    public bool IncludeSecurityHealth { get; set; } = true;
    public DateTimeOffset? LastSentAt { get; set; }
    public DateTimeOffset NextRunAt { get; set; } = DateTimeOffset.UtcNow.AddDays(7);
    public bool IsActive { get; set; } = true;
}
