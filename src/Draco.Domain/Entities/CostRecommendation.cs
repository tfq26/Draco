namespace Draco.Domain.Entities;

public class CostRecommendation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ResourceId { get; set; } = string.Empty;
    public string ResourceName { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
    public string RecommendationType { get; set; } = string.Empty; // e.g., Unused, RightSize, Idle
    public string Description { get; set; } = string.Empty;
    public decimal PotentialSavings { get; set; }
    public string Currency { get; set; } = "USD";
    public string Status { get; set; } = "Pending"; // Pending, Applied, Dismissed
    public DateTimeOffset DiscoveredAt { get; set; } = DateTimeOffset.UtcNow;
}
