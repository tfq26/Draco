namespace Draco.Domain.Entities;

public class CostBudget
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty; // Azure, AWS
    public string SubscriptionId { get; set; } = string.Empty;
    public string BudgetSource { get; set; } = "Manual"; // Manual, AzureImported, AwsImported
    public string? ExternalBudgetId { get; set; }
    public string Scope { get; set; } = string.Empty;
    public string ScopeType { get; set; } = "Subscription";
    public string? ScopeDisplayName { get; set; }
    public decimal Amount { get; set; }
    public decimal? CurrentSpend { get; set; }
    public decimal? ForecastSpend { get; set; }
    public string Currency { get; set; } = "USD";
    public string TimeGrain { get; set; } = "Monthly"; // Monthly, Quarterly, Annually
    public double AlertThresholdPercentage { get; set; } = 80.0;
    public string? NotificationSettingsJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSyncedAt { get; set; }
    public bool IsActive { get; set; } = true;
}
