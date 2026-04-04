namespace Draco.Application.Models;

public sealed class CloudConnectionSyncOutcome
{
    public int ConnectionId { get; init; }
    public string Provider { get; init; } = string.Empty;
    public string SubscriptionId { get; init; } = string.Empty;
    public bool Success { get; init; }
    public int Resources { get; init; }
    public int Budgets { get; init; }
    public int Metrics { get; init; }
    public int ResourceCosts { get; init; }
    public int ActualResourceCosts { get; init; }
    public int Recommendations { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class CloudConnectionSyncResult
{
    public int Connections { get; init; }
    public IReadOnlyList<CloudConnectionSyncOutcome> Results { get; init; } = [];
    public NotificationRefreshResult? Notifications { get; init; }
}
