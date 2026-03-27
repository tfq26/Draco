using System.Text.Json;
using Draco.Application.Interfaces;
using Draco.Application.Models;
using Draco.Domain.Entities;
using Draco.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Draco.Infrastructure.Services;

public class NotificationEvaluationService : INotificationEvaluationService
{
    private readonly DracoDbContext _dbContext;
    private readonly IEnumerable<INotificationRule> _rules;
    private readonly ILogger<NotificationEvaluationService> _logger;

    public NotificationEvaluationService(
        DracoDbContext dbContext,
        IEnumerable<INotificationRule> rules,
        ILogger<NotificationEvaluationService> logger)
    {
        _dbContext = dbContext;
        _rules = rules;
        _logger = logger;
    }

    public async Task<NotificationRefreshResult> RefreshUserNotificationsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var connections = await _dbContext.CloudConnections
            .AsNoTracking()
            .Where(connection => connection.UserId == userId && connection.IsActive)
            .ToListAsync(cancellationToken);

        var subscriptionIds = connections
            .Select(connection => connection.SubscriptionId)
            .Where(subscriptionId => !string.IsNullOrWhiteSpace(subscriptionId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var resources = subscriptionIds.Count == 0
            ? []
            : await _dbContext.CloudResources
                .AsNoTracking()
                .Where(resource => subscriptionIds.Contains(resource.SubscriptionId))
                .ToListAsync(cancellationToken);

        var resourceIds = resources
            .Select(resource => resource.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var budgets = await _dbContext.CostBudgets
            .AsNoTracking()
            .Where(budget => budget.UserId == userId && budget.IsActive)
            .ToListAsync(cancellationToken);

        var resourceCosts = resourceIds.Count == 0
            ? []
            : (await _dbContext.CloudResourceCosts
                .AsNoTracking()
                .Where(cost => cost.UserId == userId && resourceIds.Contains(cost.ResourceId))
                .OrderByDescending(cost => cost.PeriodEnd)
                .ThenByDescending(cost => cost.CapturedAt)
                .ToListAsync(cancellationToken))
                .GroupBy(cost => cost.ResourceId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

        var metrics = resourceIds.Count == 0
            ? []
            : await _dbContext.ObservabilityMetrics
                .AsNoTracking()
                .Where(metric =>
                    resourceIds.Contains(metric.ResourceId) &&
                    metric.Timestamp >= DateTimeOffset.UtcNow.AddDays(-7))
                .OrderByDescending(metric => metric.Timestamp)
                .ToListAsync(cancellationToken);

        var currentSpendByScope = resourceCosts
            .GroupBy(cost => $"{cost.Provider}:{cost.SubscriptionId}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(cost => cost.Amount),
                StringComparer.OrdinalIgnoreCase);

        var context = new NotificationEvaluationContext
        {
            UserId = userId,
            Connections = connections,
            Resources = resources,
            ResourceCosts = resourceCosts,
            Budgets = budgets,
            Metrics = metrics,
            CurrentSpendByScope = currentSpendByScope,
            EvaluatedAt = DateTimeOffset.UtcNow
        };

        var candidates = _rules
            .SelectMany(rule => rule.Evaluate(context))
            .GroupBy(candidate => candidate.NotificationKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(candidate => SeverityScore(candidate.Severity))
                .First())
            .ToList();

        var existingNotifications = await _dbContext.SystemNotifications
            .Where(notification =>
                notification.UserId == userId &&
                notification.SourceRule != null &&
                notification.SourceRule != string.Empty)
            .ToListAsync(cancellationToken);

        var activeKeys = candidates
            .Select(candidate => candidate.NotificationKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var notificationMap = existingNotifications.ToDictionary(
            notification => notification.NotificationKey,
            StringComparer.OrdinalIgnoreCase);

        var createdCount = 0;
        var updatedCount = 0;
        var resolvedCount = 0;
        var now = DateTime.UtcNow;

        foreach (var candidate in candidates)
        {
            if (!notificationMap.TryGetValue(candidate.NotificationKey, out var notification))
            {
                _dbContext.SystemNotifications.Add(new SystemNotification
                {
                    UserId = userId,
                    NotificationKey = candidate.NotificationKey,
                    Title = candidate.Title,
                    Message = candidate.Message,
                    Type = candidate.Type,
                    Severity = candidate.Severity,
                    Category = candidate.Category,
                    Provider = candidate.Provider,
                    SubscriptionId = candidate.SubscriptionId,
                    ResourceId = candidate.ResourceId,
                    Service = candidate.Service,
                    ResourceUrl = candidate.ResourceUrl,
                    SourceRule = candidate.SourceRule,
                    Metadata = candidate.Metadata,
                    CreatedAt = now,
                    LastEvaluatedAt = now,
                    IsRead = false
                });

                createdCount++;
                continue;
            }

            var wasResolved = notification.ResolvedAt.HasValue;
            notification.Title = candidate.Title;
            notification.Message = candidate.Message;
            notification.Type = candidate.Type;
            notification.Severity = candidate.Severity;
            notification.Category = candidate.Category;
            notification.Provider = candidate.Provider;
            notification.SubscriptionId = candidate.SubscriptionId;
            notification.ResourceId = candidate.ResourceId;
            notification.Service = candidate.Service;
            notification.ResourceUrl = candidate.ResourceUrl;
            notification.SourceRule = candidate.SourceRule;
            notification.Metadata = candidate.Metadata;
            notification.LastEvaluatedAt = now;
            notification.ResolvedAt = null;

            if (wasResolved)
            {
                notification.CreatedAt = now;
                notification.IsRead = false;
            }

            updatedCount++;
        }

        foreach (var notification in existingNotifications.Where(notification =>
                     !activeKeys.Contains(notification.NotificationKey) &&
                     !notification.ResolvedAt.HasValue))
        {
            notification.ResolvedAt = now;
            notification.LastEvaluatedAt = now;
            resolvedCount++;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Notification refresh completed for user {UserId}. Active: {ActiveCount}, created: {CreatedCount}, updated: {UpdatedCount}, resolved: {ResolvedCount}",
            userId,
            candidates.Count,
            createdCount,
            updatedCount,
            resolvedCount);

        return new NotificationRefreshResult
        {
            ActiveCount = candidates.Count,
            CreatedCount = createdCount,
            UpdatedCount = updatedCount,
            ResolvedCount = resolvedCount
        };
    }

    private static int SeverityScore(string severity) =>
        severity switch
        {
            "Critical" => 4,
            "High" => 3,
            "Medium" => 2,
            "Low" => 1,
            _ => 0
        };
}

public sealed class BudgetThresholdNotificationRule : INotificationRule
{
    public string RuleId => "budget-threshold";

    public IEnumerable<string> GetRequiredMetricNames(CloudResource resource) => [];

    public IEnumerable<NotificationCandidate> Evaluate(NotificationEvaluationContext context)
    {
        foreach (var budget in context.Budgets.Where(budget => budget.IsActive && budget.Amount > 0))
        {
            var currentSpend = budget.CurrentSpend ?? context.GetCurrentSpend(budget.Provider, budget.SubscriptionId);
            var forecastSpend = budget.ForecastSpend;
            var consumedPercentage = decimal.ToDouble(decimal.Round(currentSpend / budget.Amount * 100, 2));
            var importedNotifications = ParseBudgetNotifications(budget);
            var thresholds = importedNotifications.Count > 0
                ? importedNotifications.Where(notification => notification.Enabled && notification.ThresholdPercentage > 0).ToList()
                : [new ProviderBudgetNotificationSnapshot
                {
                    Name = "default",
                    ThresholdPercentage = budget.AlertThresholdPercentage <= 0 ? 80 : budget.AlertThresholdPercentage,
                    ThresholdType = "Actual",
                    Enabled = true
                }];

            foreach (var threshold in thresholds)
            {
                var thresholdType = string.IsNullOrWhiteSpace(threshold.ThresholdType) ? "Actual" : threshold.ThresholdType;
                var comparisonAmount = string.Equals(thresholdType, "Forecasted", StringComparison.OrdinalIgnoreCase) && forecastSpend.HasValue
                    ? forecastSpend.Value
                    : currentSpend;
                var comparisonPercentage = decimal.ToDouble(decimal.Round(comparisonAmount / budget.Amount * 100, 2));

                if (comparisonPercentage < threshold.ThresholdPercentage)
                {
                    continue;
                }

                var severity = comparisonPercentage >= 100
                    ? "Critical"
                    : comparisonPercentage >= threshold.ThresholdPercentage + 10
                        ? "High"
                        : "Medium";
                var statusText = comparisonPercentage >= 100 ? "exceeded" : "approaching";
                var reminderTarget = string.Equals(thresholdType, "Forecasted", StringComparison.OrdinalIgnoreCase) && forecastSpend.HasValue
                    ? $"Forecast spend is {FormatCurrency(comparisonAmount, budget.Currency)}"
                    : $"Spend is {FormatCurrency(comparisonAmount, budget.Currency)}";
                var reminderNameSuffix = string.IsNullOrWhiteSpace(threshold.Name) || string.Equals(threshold.Name, "default", StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : $" ({threshold.Name})";

                yield return new NotificationCandidate
                {
                    NotificationKey = $"budget:{budget.Id}:{thresholdType}:{threshold.ThresholdPercentage:F2}:{threshold.Name}",
                    Title = $"{budget.Name}{reminderNameSuffix} is {statusText} its limit",
                    Message = $"{reminderTarget} against a {FormatCurrency(budget.Amount, budget.Currency)} budget ({comparisonPercentage:F1}% used, threshold {threshold.ThresholdPercentage:F0}%).",
                    Type = severity == "Critical" ? "Error" : "Warning",
                    Severity = severity,
                    Category = "Budget",
                    Provider = budget.Provider,
                    SubscriptionId = budget.SubscriptionId,
                    Service = "Budget",
                    ResourceUrl = "/governance",
                    SourceRule = RuleId,
                    Metadata = JsonSerializer.Serialize(new
                    {
                        budgetId = budget.Id,
                        budgetSource = budget.BudgetSource,
                        budget.Scope,
                        budget.ScopeType,
                        budget.ScopeDisplayName,
                        currentSpend,
                        forecastSpend,
                        budget.Amount,
                        comparisonAmount,
                        comparisonPercentage,
                        threshold = threshold.ThresholdPercentage,
                        thresholdType,
                        threshold.Name,
                        threshold.ContactEmails,
                        threshold.ContactRoles,
                        threshold.ContactGroups
                    })
                };
            }
        }
    }

    private static List<ProviderBudgetNotificationSnapshot> ParseBudgetNotifications(CostBudget budget)
    {
        if (string.IsNullOrWhiteSpace(budget.NotificationSettingsJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<ProviderBudgetNotificationSnapshot>>(budget.NotificationSettingsJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string FormatCurrency(decimal amount, string currency) =>
        string.IsNullOrWhiteSpace(currency) ? amount.ToString("0.##") : $"{amount:0.##} {currency}";
}

public sealed class ComputeResourceNotificationRule : INotificationRule
{
    private const double CpuThreshold = 80;
    private const double MemoryThreshold = 85;
    private const double NetworkThresholdBytes = 5_000_000_000d;

    public string RuleId => "compute-resource-health";

    public IEnumerable<string> GetRequiredMetricNames(CloudResource resource)
    {
        if (!AppliesTo(resource))
        {
            return [];
        }

        return
        [
            NotificationMetricKeys.CpuUtilizationPercent,
            NotificationMetricKeys.MemoryUtilizationPercent,
            NotificationMetricKeys.NetworkInBytes,
            NotificationMetricKeys.NetworkOutBytes
        ];
    }

    public IEnumerable<NotificationCandidate> Evaluate(NotificationEvaluationContext context)
    {
        foreach (var resource in context.Resources.Where(AppliesTo))
        {
            var cpuSeries = context.GetMetricSeries(resource.Id, NotificationMetricKeys.CpuUtilizationPercent);
            if (cpuSeries.Count > 0)
            {
                var cpuAverage = cpuSeries.Average(metric => metric.Value);
                if (cpuAverage >= CpuThreshold)
                {
                    yield return BuildCandidate(
                        resource,
                        "cpu",
                        "Compute CPU is elevated",
                        $"Average CPU utilization is {cpuAverage:F1}% over the recent sync window.",
                        cpuAverage >= 90 ? "High" : "Medium");
                }
            }

            var memorySeries = context.GetMetricSeries(resource.Id, NotificationMetricKeys.MemoryUtilizationPercent);
            if (memorySeries.Count > 0)
            {
                var memoryAverage = memorySeries.Average(metric => metric.Value);
                if (memoryAverage >= MemoryThreshold)
                {
                    yield return BuildCandidate(
                        resource,
                        "memory",
                        "Compute memory pressure detected",
                        $"Average memory utilization is {memoryAverage:F1}% over the recent sync window.",
                        memoryAverage >= 92 ? "High" : "Medium");
                }
            }

            var networkOut = context.GetLatestMetric(resource.Id, NotificationMetricKeys.NetworkOutBytes);
            if (networkOut is not null && networkOut.Value >= NetworkThresholdBytes)
            {
                yield return BuildCandidate(
                    resource,
                    "network",
                    "Compute network egress is elevated",
                    $"Recent outbound traffic reached {FormatBytes(networkOut.Value)}.",
                    networkOut.Value >= NetworkThresholdBytes * 2 ? "High" : "Medium");
            }
        }
    }

    private static bool AppliesTo(CloudResource resource)
    {
        var normalizedType = resource.Type.Trim().ToLowerInvariant();
        return normalizedType.Contains("microsoft.compute/virtualmachines", StringComparison.Ordinal) ||
               normalizedType.Contains("aws::ec2::instance", StringComparison.Ordinal);
    }

    private static NotificationCandidate BuildCandidate(
        CloudResource resource,
        string suffix,
        string title,
        string message,
        string severity) =>
        new()
        {
            NotificationKey = $"compute:{suffix}:{resource.Id}",
            Title = $"{resource.Name}: {title}",
            Message = message,
            Type = severity == "High" ? "Error" : "Warning",
            Severity = severity,
            Category = "Performance",
            Provider = resource.Provider,
            SubscriptionId = resource.SubscriptionId,
            ResourceId = resource.Id,
            Service = "Compute",
            ResourceUrl = "/resources",
            SourceRule = "compute-resource-health",
            Metadata = JsonSerializer.Serialize(new { resource.Id, resource.Name, resource.Type })
        };

    private static string FormatBytes(double bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        var index = 0;
        var value = bytes;
        while (value >= 1024 && index < suffixes.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return $"{value:0.##} {suffixes[index]}";
    }
}

public sealed class StorageResourceNotificationRule : INotificationRule
{
    public string RuleId => "storage-resource-health";

    public IEnumerable<string> GetRequiredMetricNames(CloudResource resource)
    {
        if (!AppliesTo(resource))
        {
            return [];
        }

        return
        [
            NotificationMetricKeys.StorageCapacityBytes,
            NotificationMetricKeys.StorageObjectCount,
            NotificationMetricKeys.StorageTransactionsCount
        ];
    }

    public IEnumerable<NotificationCandidate> Evaluate(NotificationEvaluationContext context)
    {
        foreach (var resource in context.Resources.Where(AppliesTo))
        {
            var capacitySeries = context.GetMetricSeries(resource.Id, NotificationMetricKeys.StorageCapacityBytes);
            if (capacitySeries.Count >= 2)
            {
                var initial = capacitySeries.First().Value;
                var latest = capacitySeries.Last().Value;
                var growthRatio = initial <= 0 ? 0 : (latest - initial) / initial;

                if (latest >= 100_000_000_000d && growthRatio >= 0.2d)
                {
                    yield return BuildCandidate(
                        resource,
                        "capacity",
                        "Storage capacity is growing quickly",
                        $"Storage usage grew by {(growthRatio * 100):F1}% and is now {FormatBytes(latest)}.",
                        growthRatio >= 0.4d ? "High" : "Medium");
                }
            }

            var transactionMetric = context.GetLatestMetric(resource.Id, NotificationMetricKeys.StorageTransactionsCount);
            if (transactionMetric is not null && transactionMetric.Value >= 100_000d)
            {
                yield return BuildCandidate(
                    resource,
                    "transactions",
                    "Storage transaction volume is elevated",
                    $"Recent storage transactions reached {transactionMetric.Value:N0}.",
                    transactionMetric.Value >= 500_000d ? "High" : "Medium");
            }
        }
    }

    private static bool AppliesTo(CloudResource resource)
    {
        var normalizedType = resource.Type.Trim().ToLowerInvariant();
        return normalizedType.Contains("microsoft.storage/storageaccounts", StringComparison.Ordinal) ||
               normalizedType.Contains("aws::s3::bucket", StringComparison.Ordinal);
    }

    private static NotificationCandidate BuildCandidate(
        CloudResource resource,
        string suffix,
        string title,
        string message,
        string severity) =>
        new()
        {
            NotificationKey = $"storage:{suffix}:{resource.Id}",
            Title = $"{resource.Name}: {title}",
            Message = message,
            Type = severity == "High" ? "Error" : "Warning",
            Severity = severity,
            Category = "Storage",
            Provider = resource.Provider,
            SubscriptionId = resource.SubscriptionId,
            ResourceId = resource.Id,
            Service = "Storage",
            ResourceUrl = "/resources",
            SourceRule = "storage-resource-health",
            Metadata = JsonSerializer.Serialize(new { resource.Id, resource.Name, resource.Type })
        };

    private static string FormatBytes(double bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        var index = 0;
        var value = bytes;
        while (value >= 1024 && index < suffixes.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return $"{value:0.##} {suffixes[index]}";
    }
}

public sealed class FunctionResourceNotificationRule : INotificationRule
{
    public string RuleId => "function-resource-health";

    public IEnumerable<string> GetRequiredMetricNames(CloudResource resource)
    {
        if (!AppliesTo(resource))
        {
            return [];
        }

        return
        [
            NotificationMetricKeys.FunctionErrorsCount,
            NotificationMetricKeys.FunctionDurationMilliseconds,
            NotificationMetricKeys.FunctionInvocationsCount
        ];
    }

    public IEnumerable<NotificationCandidate> Evaluate(NotificationEvaluationContext context)
    {
        foreach (var resource in context.Resources.Where(AppliesTo))
        {
            var errorMetric = context.GetLatestMetric(resource.Id, NotificationMetricKeys.FunctionErrorsCount);
            var invocationMetric = context.GetLatestMetric(resource.Id, NotificationMetricKeys.FunctionInvocationsCount);
            if (errorMetric is not null && errorMetric.Value > 0)
            {
                yield return BuildCandidate(
                    resource,
                    "errors",
                    "Function errors detected",
                    invocationMetric is null
                        ? $"Recent sync captured {errorMetric.Value:N0} function errors."
                        : $"Recent sync captured {errorMetric.Value:N0} function errors across {invocationMetric.Value:N0} invocations.",
                    errorMetric.Value >= 10 ? "High" : "Medium");
            }

            var durationMetric = context.GetLatestMetric(resource.Id, NotificationMetricKeys.FunctionDurationMilliseconds);
            if (durationMetric is not null && durationMetric.Value >= 1_000d)
            {
                yield return BuildCandidate(
                    resource,
                    "duration",
                    "Function duration is elevated",
                    $"Average function duration is {durationMetric.Value:F0} ms.",
                    durationMetric.Value >= 2_000d ? "High" : "Medium");
            }
        }
    }

    private static bool AppliesTo(CloudResource resource)
    {
        var normalizedType = resource.Type.Trim().ToLowerInvariant();
        return normalizedType.Contains("aws::lambda::function", StringComparison.Ordinal) ||
               normalizedType.Contains("microsoft.web/sites", StringComparison.Ordinal);
    }

    private static NotificationCandidate BuildCandidate(
        CloudResource resource,
        string suffix,
        string title,
        string message,
        string severity) =>
        new()
        {
            NotificationKey = $"function:{suffix}:{resource.Id}",
            Title = $"{resource.Name}: {title}",
            Message = message,
            Type = severity == "High" ? "Error" : "Warning",
            Severity = severity,
            Category = "Serverless",
            Provider = resource.Provider,
            SubscriptionId = resource.SubscriptionId,
            ResourceId = resource.Id,
            Service = "Function",
            ResourceUrl = "/resources",
            SourceRule = "function-resource-health",
            Metadata = JsonSerializer.Serialize(new { resource.Id, resource.Name, resource.Type })
        };
}
