namespace Draco.Application.Models;

public sealed class PreparedInsightContext
{
    public Guid UserId { get; init; }
    public string UserName { get; init; } = string.Empty;
    public string? Email { get; init; }
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
    public InsightOverview Overview { get; init; } = new();
    public IReadOnlyList<InsightConnectionHealth> Connections { get; init; } = [];
    public IReadOnlyList<InsightProviderBreakdown> ProviderBreakdown { get; init; } = [];
    public IReadOnlyList<InsightResourceTypeBreakdown> ResourceTypeBreakdown { get; init; } = [];
    public IReadOnlyList<InsightCostBreakdown> CostBreakdown { get; init; } = [];
    public IReadOnlyList<InsightProviderCostBreakdown> ProviderCostBreakdown { get; init; } = [];
    public IReadOnlyList<InsightResourceGroupCostBreakdown> ResourceGroupCostBreakdown { get; init; } = [];
    public IReadOnlyList<InsightResourceCostBreakdown> ResourceCostBreakdown { get; init; } = [];
    public IReadOnlyList<InsightBudgetStatus> Budgets { get; init; } = [];
    public IReadOnlyList<InsightRecommendation> Recommendations { get; init; } = [];
    public IReadOnlyList<InsightAnomaly> Anomalies { get; init; } = [];
    public IReadOnlyList<InsightWorkflowSuggestion> WorkflowSuggestions { get; init; } = [];
}

public sealed class InsightOverview
{
    public int ConnectionCount { get; init; }
    public int ProviderCount { get; init; }
    public int SubscriptionCount { get; init; }
    public int ResourceCount { get; init; }
    public int RecommendationCount { get; init; }
    public int OpenAlertCount { get; init; }
    public int AnomalyCount { get; init; }
    public decimal CurrentMonthlyCost { get; init; }
    public decimal ForecastMonthlyCost { get; init; }
    public decimal ActualMonthlyCost { get; init; }
    public decimal EstimatedMonthlyCost { get; init; }
    public decimal BudgetForecastMonthlyCost { get; init; }
    public bool HasEstimatedFallbackCosts { get; init; }
    public decimal PotentialMonthlySavings { get; init; }
    public DateTimeOffset? LastSyncedAt { get; init; }
}

public sealed class InsightConnectionHealth
{
    public int ConnectionId { get; init; }
    public string Provider { get; init; } = string.Empty;
    public string SubscriptionId { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public bool IsActive { get; init; }
    public DateTimeOffset ConnectedAt { get; init; }
    public DateTimeOffset? LastSyncedAt { get; init; }
    public string SyncStatus { get; init; } = string.Empty;
    public string? SyncMessage { get; init; }
}

public sealed class InsightProviderBreakdown
{
    public string Provider { get; init; } = string.Empty;
    public int ResourceCount { get; init; }
    public int SubscriptionCount { get; init; }
}

public sealed class InsightResourceTypeBreakdown
{
    public string Type { get; init; } = string.Empty;
    public int Count { get; init; }
}

public sealed class InsightCostBreakdown
{
    public string Provider { get; init; } = string.Empty;
    public string SubscriptionId { get; init; } = string.Empty;
    public string Currency { get; init; } = "USD";
    public decimal CurrentAmount { get; init; }
    public decimal? PreviousAmount { get; init; }
    public decimal? DeltaAmount { get; init; }
    public double? DeltaPercentage { get; init; }
    public string Granularity { get; init; } = "Monthly";
    public DateTimeOffset PeriodStart { get; init; }
    public DateTimeOffset PeriodEnd { get; init; }
}

public sealed class InsightProviderCostBreakdown
{
    public string Provider { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public string Currency { get; init; } = "USD";
    public int ResourceCount { get; init; }
}

public sealed class InsightResourceGroupCostBreakdown
{
    public string Provider { get; init; } = string.Empty;
    public string ResourceGroupName { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public string Currency { get; init; } = "USD";
    public int ResourceCount { get; init; }
}

public sealed class InsightResourceCostBreakdown
{
    public string ResourceId { get; init; } = string.Empty;
    public string ResourceName { get; init; } = string.Empty;
    public string ResourceType { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string SubscriptionId { get; init; } = string.Empty;
    public string ResourceGroupName { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "USD";
    public string CostSource { get; init; } = "Estimated";
    public DateTimeOffset CapturedAt { get; init; }
}

public sealed class InsightBudgetStatus
{
    public Guid BudgetId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string SubscriptionId { get; init; } = string.Empty;
    public decimal LimitAmount { get; init; }
    public decimal CurrentAmount { get; init; }
    public decimal? ForecastAmount { get; init; }
    public decimal RemainingAmount { get; init; }
    public double AlertThresholdPercentage { get; init; }
    public double ConsumedPercentage { get; init; }
    public string Currency { get; init; } = "USD";
    public string Status { get; init; } = string.Empty;
}

public sealed class InsightRecommendation
{
    public Guid Id { get; init; }
    public string Provider { get; init; } = string.Empty;
    public string SubscriptionId { get; init; } = string.Empty;
    public string ResourceId { get; init; } = string.Empty;
    public string ResourceName { get; init; } = string.Empty;
    public string RecommendationType { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public decimal PotentialSavings { get; init; }
    public string Currency { get; init; } = "USD";
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset DiscoveredAt { get; init; }
}

public sealed class InsightAnomaly
{
    public string Id { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string? SubscriptionId { get; init; }
    public string? ResourceId { get; init; }
    public string DetectionMethod { get; init; } = string.Empty;
    public decimal? CurrentValue { get; init; }
    public decimal? BaselineValue { get; init; }
    public string? Unit { get; init; }
}

public sealed class InsightWorkflowSuggestion
{
    public string Id { get; init; } = string.Empty;
    public string Trigger { get; init; } = string.Empty;
    public string SuggestedAction { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string? SubscriptionId { get; init; }
    public string? ResourceId { get; init; }
    public bool CanAutoRun { get; init; }
}
