namespace Draco.Domain.Entities;

public class CostBudget
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty; // Azure, AWS
    public string SubscriptionId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string TimeGrain { get; set; } = "Monthly"; // Monthly, Quarterly, Annually
    public double AlertThresholdPercentage { get; set; } = 80.0;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsActive { get; set; } = true;
}
