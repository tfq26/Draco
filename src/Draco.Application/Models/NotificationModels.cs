using Draco.Domain.Entities;

namespace Draco.Application.Models;

public static class NotificationMetricKeys
{
    public const string CpuUtilizationPercent = "cpu.utilization.percent";
    public const string MemoryUtilizationPercent = "memory.utilization.percent";
    public const string NetworkInBytes = "network.in.bytes";
    public const string NetworkOutBytes = "network.out.bytes";
    public const string StorageCapacityBytes = "storage.capacity.bytes";
    public const string StorageObjectCount = "storage.object.count";
    public const string StorageTransactionsCount = "storage.transactions.count";
    public const string FunctionErrorsCount = "function.errors.count";
    public const string FunctionDurationMilliseconds = "function.duration.ms";
    public const string FunctionInvocationsCount = "function.invocations.count";
}

public sealed class ProviderBudgetSnapshot
{
    public string Name { get; init; } = string.Empty;
    public string ExternalBudgetId { get; init; } = string.Empty;
    public string Scope { get; init; } = string.Empty;
    public string ScopeType { get; init; } = "Subscription";
    public string? ScopeDisplayName { get; init; }
    public decimal Amount { get; init; }
    public decimal? CurrentSpend { get; init; }
    public decimal? ForecastSpend { get; init; }
    public string Currency { get; init; } = "USD";
    public string TimeGrain { get; init; } = "Monthly";
    public double? AlertThresholdPercentage { get; init; }
    public IReadOnlyList<ProviderBudgetNotificationSnapshot> Notifications { get; init; } = [];
    public string Source { get; init; } = "Manual";
}

public sealed class ProviderBudgetNotificationSnapshot
{
    public string Name { get; init; } = string.Empty;
    public double ThresholdPercentage { get; init; }
    public string ThresholdType { get; init; } = "Actual";
    public string Operator { get; init; } = "GreaterThan";
    public bool Enabled { get; init; } = true;
    public IReadOnlyList<string> ContactEmails { get; init; } = [];
    public IReadOnlyList<string> ContactRoles { get; init; } = [];
    public IReadOnlyList<string> ContactGroups { get; init; } = [];
}

public sealed class NotificationCandidate
{
    public string NotificationKey { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Type { get; init; } = "Info";
    public string Severity { get; init; } = "Info";
    public string Category { get; init; } = "System";
    public string? Provider { get; init; }
    public string? SubscriptionId { get; init; }
    public string? ResourceId { get; init; }
    public string? Service { get; init; }
    public string? ResourceUrl { get; init; }
    public string SourceRule { get; init; } = string.Empty;
    public string? Metadata { get; init; }
}

public sealed class NotificationRefreshResult
{
    public int ActiveCount { get; init; }
    public int CreatedCount { get; init; }
    public int UpdatedCount { get; init; }
    public int ResolvedCount { get; init; }
}

public sealed class NotificationEvaluationContext
{
    public Guid UserId { get; init; }
    public IReadOnlyList<CloudConnection> Connections { get; init; } = [];
    public IReadOnlyList<CloudResource> Resources { get; init; } = [];
    public IReadOnlyList<CloudResourceCost> ResourceCosts { get; init; } = [];
    public IReadOnlyList<CostBudget> Budgets { get; init; } = [];
    public IReadOnlyList<CostRecommendation> Recommendations { get; init; } = [];
    public IReadOnlyList<ObservabilityMetric> Metrics { get; init; } = [];
    public IReadOnlyDictionary<string, decimal> CurrentSpendByScope { get; init; } = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
    public DateTimeOffset EvaluatedAt { get; init; } = DateTimeOffset.UtcNow;

    public decimal GetCurrentSpend(string provider, string subscriptionId) =>
        CurrentSpendByScope.TryGetValue($"{provider}:{subscriptionId}", out var amount)
            ? amount
            : 0m;

    public IReadOnlyList<ObservabilityMetric> GetMetricSeries(string resourceId, string metricName) =>
        Metrics
            .Where(metric =>
                string.Equals(metric.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(metric.MetricName, metricName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(metric => metric.Timestamp)
            .ToList();

    public ObservabilityMetric? GetLatestMetric(string resourceId, string metricName) =>
        GetMetricSeries(resourceId, metricName).LastOrDefault();
}
