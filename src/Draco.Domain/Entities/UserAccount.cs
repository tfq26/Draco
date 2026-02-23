using System.ComponentModel.DataAnnotations;

namespace Draco.Domain.Entities;

public class UserAccount
{
    [Key]
    public string Phone { get; set; } = string.Empty; // Primary key is the phone number
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? AuthId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation property for connections
    public List<CloudConnection> Connections { get; set; } = new();
    public List<PulseReportSchedule> ReportSchedules { get; set; } = new();
}

public class CloudConnection
{
    public int Id { get; set; }
    public string UserPhone { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty; // "Azure", "AWS"
    public string SubscriptionId { get; set; } = string.Empty;
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTimeOffset? TokenExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset ConnectedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class PulseReportSchedule
{
    public int Id { get; set; }
    public string UserPhone { get; set; } = string.Empty;
    public string Frequency { get; set; } = "Weekly"; // Daily, Weekly, Monthly
    public bool IncludeCostAnalysis { get; set; } = true;
    public bool IncludeSecurityHealth { get; set; } = true;
    public DateTimeOffset LastSentAt { get; set; }
    public DateTimeOffset NextRunAt { get; set; } = DateTimeOffset.UtcNow.AddDays(7);
    public bool IsActive { get; set; } = true;
}
