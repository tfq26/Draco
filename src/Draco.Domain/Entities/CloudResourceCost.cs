namespace Draco.Domain.Entities;

public class CloudResourceCost
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public UserAccount? User { get; set; }
    public string ResourceId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
    public string ResourceGroupName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string Granularity { get; set; } = "Monthly";
    public DateTimeOffset PeriodStart { get; set; } = DateTimeOffset.UtcNow.Date;
    public DateTimeOffset PeriodEnd { get; set; } = DateTimeOffset.UtcNow.Date;
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
    public string CostSource { get; set; } = "Estimated";
    public string? RawData { get; set; }
}
