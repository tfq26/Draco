using System.Text.Json;
using Draco.Application.Interfaces;
using Draco.Application.Models;
using Draco.Domain.Entities;
using Draco.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Draco.Infrastructure.Services;

public class InsightContextService : IInsightContextService
{
    private static readonly JsonSerializerOptions ModelJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly DracoDbContext _dbContext;
    private readonly ILogger<InsightContextService> _logger;

    public InsightContextService(DracoDbContext dbContext, ILogger<InsightContextService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<PreparedInsightContext?> BuildForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Building prepared insight context for user {UserId}", userId);

        var user = await _dbContext.UserAccounts
            .AsNoTracking()
            .Include(account => account.Connections)
            .Include(account => account.ReportSchedules)
            .FirstOrDefaultAsync(account => account.Id == userId, cancellationToken);

        if (user is null)
        {
            return null;
        }

        var activeConnections = user.Connections
            .Where(connection => connection.IsActive)
            .OrderByDescending(connection => connection.LastSyncedAt ?? connection.ConnectedAt)
            .ToList();

        var subscriptionIds = activeConnections
            .Select(connection => connection.SubscriptionId)
            .Where(subscriptionId => !string.IsNullOrWhiteSpace(subscriptionId))
            .Distinct()
            .ToList();

        var resources = subscriptionIds.Count == 0
            ? []
            : await _dbContext.CloudResources
                .AsNoTracking()
                .Where(resource => subscriptionIds.Contains(resource.SubscriptionId))
                .ToListAsync(cancellationToken);

        var resourceIds = resources.Select(resource => resource.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var recommendations = subscriptionIds.Count == 0
            ? []
            : await _dbContext.CostRecommendations
                .AsNoTracking()
                .Where(recommendation =>
                    subscriptionIds.Contains(recommendation.SubscriptionId) ||
                    (!string.IsNullOrWhiteSpace(recommendation.ResourceId) && resourceIds.Contains(recommendation.ResourceId)))
                .OrderByDescending(recommendation => recommendation.PotentialSavings)
                .ToListAsync(cancellationToken);

        var costSnapshots = await _dbContext.CloudCostSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.UserId == user.Id)
            .OrderByDescending(snapshot => snapshot.PeriodEnd)
            .ToListAsync(cancellationToken);

        var latestResourceCosts = subscriptionIds.Count == 0
            ? []
            : (await _dbContext.CloudResourceCosts
                .AsNoTracking()
                .Where(cost =>
                    cost.UserId == user.Id &&
                    subscriptionIds.Contains(cost.SubscriptionId))
                .OrderByDescending(cost => cost.PeriodEnd)
                .ThenByDescending(cost => cost.CapturedAt)
                .ToListAsync(cancellationToken))
                .GroupBy(cost => cost.ResourceId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(cost => cost.PeriodEnd)
                    .ThenByDescending(cost => cost.CapturedAt)
                    .First())
                .ToList();

        var budgets = await _dbContext.CostBudgets
            .AsNoTracking()
            .Where(budget => budget.UserId == user.Id)
            .OrderByDescending(budget => budget.CreatedAt)
            .ToListAsync(cancellationToken);

        var recentMetrics = resourceIds.Count == 0
            ? []
            : await _dbContext.ObservabilityMetrics
                .AsNoTracking()
                .Where(metric =>
                    resourceIds.Contains(metric.ResourceId) &&
                    metric.Timestamp >= DateTimeOffset.UtcNow.AddDays(-7))
                .OrderByDescending(metric => metric.Timestamp)
                .Take(500)
                .ToListAsync(cancellationToken);

        var connectionHealth = activeConnections.Select(connection => new InsightConnectionHealth
        {
            ConnectionId = connection.Id,
            Provider = connection.Provider,
            SubscriptionId = connection.SubscriptionId,
            DisplayName = connection.DisplayName,
            IsActive = connection.IsActive,
            ConnectedAt = connection.ConnectedAt,
            LastSyncedAt = connection.LastSyncedAt,
            SyncStatus = connection.SyncStatus,
            SyncMessage = connection.SyncMessage
        }).ToList();

        var providerBreakdown = resources
            .GroupBy(resource => resource.Provider)
            .Select(group => new InsightProviderBreakdown
            {
                Provider = group.Key,
                ResourceCount = group.Count(),
                SubscriptionCount = group.Select(resource => resource.SubscriptionId).Distinct().Count()
            })
            .OrderByDescending(item => item.ResourceCount)
            .ToList();

        var resourceTypeBreakdown = resources
            .GroupBy(resource => resource.Type)
            .Select(group => new InsightResourceTypeBreakdown
            {
                Type = group.Key,
                Count = group.Count()
            })
            .OrderByDescending(item => item.Count)
            .Take(15)
            .ToList();

        var costBreakdown = BuildCostBreakdown(costSnapshots);
        var preferredRollupResourceCosts = SelectPreferredRollupCosts(latestResourceCosts);
        var providerCostBreakdown = BuildProviderCostBreakdown(preferredRollupResourceCosts);
        var resourceGroupCostBreakdown = BuildResourceGroupCostBreakdown(preferredRollupResourceCosts);
        var resourceCostBreakdown = BuildResourceCostBreakdown(resources, preferredRollupResourceCosts);
        var budgetStatuses = BuildBudgetStatuses(budgets, costBreakdown);
        var insightRecommendations = recommendations.Select(recommendation => new InsightRecommendation
        {
            Id = recommendation.Id,
            Provider = recommendation.Provider,
            SubscriptionId = recommendation.SubscriptionId,
            ResourceId = recommendation.ResourceId,
            ResourceName = recommendation.ResourceName,
            RecommendationType = recommendation.RecommendationType,
            Description = recommendation.Description,
            PotentialSavings = recommendation.PotentialSavings,
            Currency = recommendation.Currency,
            Status = recommendation.Status,
            DiscoveredAt = recommendation.DiscoveredAt
        }).ToList();

        var anomalies = BuildAnomalies(connectionHealth, costBreakdown, budgetStatuses, recentMetrics, insightRecommendations);
        var workflowSuggestions = BuildWorkflowSuggestions(anomalies, insightRecommendations);

        var latestSyncAt = connectionHealth
            .Where(connection => connection.LastSyncedAt.HasValue)
            .Select(connection => connection.LastSyncedAt)
            .Max();

        var overview = new InsightOverview
        {
            ConnectionCount = connectionHealth.Count,
            ProviderCount = connectionHealth.Select(connection => connection.Provider).Distinct().Count(),
            SubscriptionCount = subscriptionIds.Count,
            ResourceCount = resources.Count,
            RecommendationCount = insightRecommendations.Count,
            OpenAlertCount = insightRecommendations.Count(recommendation => recommendation.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase)),
            AnomalyCount = anomalies.Count,
            CurrentMonthlyCost = providerCostBreakdown.Count > 0
                ? providerCostBreakdown.Sum(item => item.TotalAmount)
                : costBreakdown.Sum(item => item.CurrentAmount),
            ForecastMonthlyCost = ForecastMonthlyCost(costSnapshots),
            PotentialMonthlySavings = insightRecommendations.Sum(recommendation => recommendation.PotentialSavings),
            LastSyncedAt = latestSyncAt
        };

        return new PreparedInsightContext
        {
            UserId = user.Id,
            UserName = user.Name,
            Email = user.Email,
            GeneratedAt = DateTimeOffset.UtcNow,
            Overview = overview,
            Connections = connectionHealth,
            ProviderBreakdown = providerBreakdown,
            ResourceTypeBreakdown = resourceTypeBreakdown,
            CostBreakdown = costBreakdown,
            ProviderCostBreakdown = providerCostBreakdown,
            ResourceGroupCostBreakdown = resourceGroupCostBreakdown,
            ResourceCostBreakdown = resourceCostBreakdown,
            Budgets = budgetStatuses,
            Recommendations = insightRecommendations,
            Anomalies = anomalies,
            WorkflowSuggestions = workflowSuggestions
        };
    }

    private static List<CloudResourceCost> SelectPreferredRollupCosts(IReadOnlyCollection<CloudResourceCost> resourceCosts) =>
        resourceCosts
            .GroupBy(cost => $"{cost.Provider}:{cost.SubscriptionId}", StringComparer.OrdinalIgnoreCase)
            .SelectMany(group =>
            {
                var actualCosts = group.Where(IsActualCostSource).ToList();
                return actualCosts.Count > 0 ? actualCosts : group.ToList();
            })
            .ToList();

    public string SerializeForModel(PreparedInsightContext context)
    {
        var payload = new
        {
            generatedAt = context.GeneratedAt,
            overview = context.Overview,
            connectionHealth = context.Connections,
            providerBreakdown = context.ProviderBreakdown,
            topCostBreakdown = context.CostBreakdown.Take(10),
            providerCostBreakdown = context.ProviderCostBreakdown,
            resourceGroupCostBreakdown = context.ResourceGroupCostBreakdown.Take(12),
            resourceCostBreakdown = context.ResourceCostBreakdown.Take(12),
            budgets = context.Budgets,
            anomalies = context.Anomalies.Take(12),
            workflowSuggestions = context.WorkflowSuggestions.Take(12),
            topRecommendations = context.Recommendations.Take(12),
            topResourceTypes = context.ResourceTypeBreakdown.Take(10)
        };

        return JsonSerializer.Serialize(payload, ModelJsonOptions);
    }

    private static List<InsightCostBreakdown> BuildCostBreakdown(IReadOnlyCollection<CloudCostSnapshot> snapshots) =>
        snapshots
            .GroupBy(snapshot => new { snapshot.Provider, snapshot.SubscriptionId, snapshot.Currency })
            .Select(group =>
            {
                var ordered = group.OrderByDescending(item => item.PeriodEnd).ToList();
                var current = ordered[0];
                var previous = ordered.Skip(1).FirstOrDefault();
                var deltaAmount = previous is null ? (decimal?)null : current.Amount - previous.Amount;
                var deltaPercentage = previous is null || previous.Amount == 0
                    ? (double?)null
                    : Math.Round((double)(deltaAmount!.Value / previous.Amount) * 100, 2);

                return new InsightCostBreakdown
                {
                    Provider = current.Provider,
                    SubscriptionId = current.SubscriptionId,
                    Currency = current.Currency,
                    CurrentAmount = current.Amount,
                    PreviousAmount = previous?.Amount,
                    DeltaAmount = deltaAmount,
                    DeltaPercentage = deltaPercentage,
                    Granularity = current.Granularity,
                    PeriodStart = current.PeriodStart,
                    PeriodEnd = current.PeriodEnd
                };
            })
            .OrderByDescending(item => item.CurrentAmount)
            .ToList();

    private static bool IsActualCostSource(CloudResourceCost cost) =>
        string.Equals(cost.CostSource, "AzureActual", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(cost.CostSource, "AwsActual", StringComparison.OrdinalIgnoreCase);

    private static List<InsightProviderCostBreakdown> BuildProviderCostBreakdown(IReadOnlyCollection<CloudResourceCost> resourceCosts) =>
        resourceCosts
            .GroupBy(cost => new { cost.Provider, cost.Currency })
            .Select(group => new InsightProviderCostBreakdown
            {
                Provider = group.Key.Provider,
                Currency = group.Key.Currency,
                TotalAmount = group.Sum(item => item.Amount),
                ResourceCount = group.Select(item => item.ResourceId).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            })
            .OrderByDescending(item => item.TotalAmount)
            .ToList();

    private static List<InsightResourceGroupCostBreakdown> BuildResourceGroupCostBreakdown(IReadOnlyCollection<CloudResourceCost> resourceCosts) =>
        resourceCosts
            .GroupBy(cost => new
            {
                cost.Provider,
                cost.Currency,
                ResourceGroupName = string.IsNullOrWhiteSpace(cost.ResourceGroupName) ? "Ungrouped" : cost.ResourceGroupName
            })
            .Select(group => new InsightResourceGroupCostBreakdown
            {
                Provider = group.Key.Provider,
                Currency = group.Key.Currency,
                ResourceGroupName = group.Key.ResourceGroupName,
                TotalAmount = group.Sum(item => item.Amount),
                ResourceCount = group.Select(item => item.ResourceId).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            })
            .OrderByDescending(item => item.TotalAmount)
            .Take(12)
            .ToList();

    private static List<InsightResourceCostBreakdown> BuildResourceCostBreakdown(
        IReadOnlyCollection<CloudResource> resources,
        IReadOnlyCollection<CloudResourceCost> resourceCosts)
    {
        var resourceMap = resources.ToDictionary(resource => resource.Id, StringComparer.OrdinalIgnoreCase);

        return resourceCosts
            .Select(cost =>
            {
                resourceMap.TryGetValue(cost.ResourceId, out var resource);

                return new InsightResourceCostBreakdown
                {
                    ResourceId = cost.ResourceId,
                    ResourceName = resource?.Name ?? cost.ResourceId,
                    ResourceType = resource?.Type ?? string.Empty,
                    Provider = cost.Provider,
                    SubscriptionId = cost.SubscriptionId,
                    ResourceGroupName = string.IsNullOrWhiteSpace(cost.ResourceGroupName) ? "Ungrouped" : cost.ResourceGroupName,
                    Amount = cost.Amount,
                    Currency = cost.Currency,
                    CostSource = cost.CostSource,
                    CapturedAt = cost.CapturedAt
                };
            })
            .OrderByDescending(item => item.Amount)
            .Take(20)
            .ToList();
    }

    private static List<InsightBudgetStatus> BuildBudgetStatuses(
        IReadOnlyCollection<CostBudget> budgets,
        IReadOnlyCollection<InsightCostBreakdown> costBreakdown)
    {
        var currentSpendMap = costBreakdown.ToDictionary(
            item => $"{item.Provider}:{item.SubscriptionId}",
            item => item.CurrentAmount,
            StringComparer.OrdinalIgnoreCase);

        return budgets.Select(budget =>
        {
            var currentAmount = budget.CurrentSpend
                ?? currentSpendMap.GetValueOrDefault($"{budget.Provider}:{budget.SubscriptionId}", 0m);
            var consumedPercentage = budget.Amount <= 0
                ? 0
                : Math.Round((double)(currentAmount / budget.Amount) * 100, 2);

            var status = consumedPercentage >= 100
                ? "Exceeded"
                : consumedPercentage >= budget.AlertThresholdPercentage
                    ? "Warning"
                    : "Healthy";

            return new InsightBudgetStatus
            {
                BudgetId = budget.Id,
                Name = budget.Name,
                Provider = budget.Provider,
                SubscriptionId = budget.SubscriptionId,
                LimitAmount = budget.Amount,
                CurrentAmount = currentAmount,
                RemainingAmount = budget.Amount - currentAmount,
                AlertThresholdPercentage = budget.AlertThresholdPercentage,
                ConsumedPercentage = consumedPercentage,
                Currency = budget.Currency,
                Status = status
            };
        })
        .OrderByDescending(item => item.ConsumedPercentage)
        .ToList();
    }

    private static List<InsightAnomaly> BuildAnomalies(
        IReadOnlyCollection<InsightConnectionHealth> connections,
        IReadOnlyCollection<InsightCostBreakdown> costBreakdown,
        IReadOnlyCollection<InsightBudgetStatus> budgets,
        IReadOnlyCollection<ObservabilityMetric> metrics,
        IReadOnlyCollection<InsightRecommendation> recommendations)
    {
        var anomalies = new List<InsightAnomaly>();
        var now = DateTimeOffset.UtcNow;

        foreach (var connection in connections.Where(connection =>
                     !connection.LastSyncedAt.HasValue ||
                     connection.LastSyncedAt.Value <= now.AddHours(-12) ||
                     !string.Equals(connection.SyncStatus, "Healthy", StringComparison.OrdinalIgnoreCase)))
        {
            anomalies.Add(new InsightAnomaly
            {
                Id = $"connection:{connection.ConnectionId}",
                Category = "ConnectionHealth",
                Severity = "High",
                Title = $"{connection.Provider} connection requires attention",
                Summary = connection.LastSyncedAt.HasValue
                    ? $"Last sync was at {connection.LastSyncedAt:O}. Status: {connection.SyncStatus}."
                    : "Connection has never completed a successful sync.",
                Provider = connection.Provider,
                SubscriptionId = connection.SubscriptionId,
                DetectionMethod = "Connection sync health monitor"
            });
        }

        foreach (var budget in budgets.Where(budget => budget.Status is "Warning" or "Exceeded"))
        {
            anomalies.Add(new InsightAnomaly
            {
                Id = $"budget:{budget.BudgetId}",
                Category = "Budget",
                Severity = budget.Status == "Exceeded" ? "Critical" : "Medium",
                Title = $"{budget.Name} is {budget.Status.ToLowerInvariant()}",
                Summary = $"Current spend is {budget.CurrentAmount} {budget.Currency} against a {budget.LimitAmount} {budget.Currency} limit.",
                Provider = budget.Provider,
                SubscriptionId = budget.SubscriptionId,
                DetectionMethod = "Budget threshold evaluation",
                CurrentValue = budget.CurrentAmount,
                BaselineValue = budget.LimitAmount,
                Unit = budget.Currency
            });
        }

        foreach (var cost in costBreakdown.Where(cost => cost.DeltaPercentage.HasValue && cost.DeltaPercentage.Value >= 25))
        {
            anomalies.Add(new InsightAnomaly
            {
                Id = $"cost-spike:{cost.Provider}:{cost.SubscriptionId}",
                Category = "CostSpike",
                Severity = cost.DeltaPercentage >= 50 ? "High" : "Medium",
                Title = $"{cost.Provider} spend increased sharply",
                Summary = $"Current spend is {cost.CurrentAmount} {cost.Currency}, up {cost.DeltaPercentage}% from the previous comparable period.",
                Provider = cost.Provider,
                SubscriptionId = cost.SubscriptionId,
                DetectionMethod = "Cost trend delta",
                CurrentValue = cost.CurrentAmount,
                BaselineValue = cost.PreviousAmount,
                Unit = cost.Currency
            });
        }

        var hotMetricGroups = metrics
            .GroupBy(metric => new { metric.ResourceId, metric.MetricName })
            .Select(group => new
            {
                group.Key.ResourceId,
                group.Key.MetricName,
                Average = group.Average(metric => metric.Value),
                Latest = group.OrderByDescending(metric => metric.Timestamp).First(),
                Threshold = ResolveMetricThreshold(group.Key.MetricName)
            })
            .Where(item => item.Threshold.HasValue && item.Average >= item.Threshold.Value)
            .OrderByDescending(item => item.Average)
            .Take(10);

        foreach (var metric in hotMetricGroups)
        {
            anomalies.Add(new InsightAnomaly
            {
                Id = $"metric:{metric.ResourceId}:{metric.MetricName}",
                Category = "Metric",
                Severity = metric.Average >= metric.Threshold!.Value * 1.2 ? "High" : "Medium",
                Title = $"{metric.MetricName} is elevated",
                Summary = $"Average {metric.MetricName} over the recent window is {Math.Round(metric.Average, 2)} {metric.Latest.Unit}.",
                Provider = string.Empty,
                ResourceId = metric.ResourceId,
                DetectionMethod = "Metric threshold average",
                CurrentValue = Convert.ToDecimal(metric.Average),
                BaselineValue = Convert.ToDecimal(metric.Threshold.Value),
                Unit = metric.Latest.Unit
            });
        }

        foreach (var recommendation in recommendations.Where(recommendation =>
                     recommendation.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase) &&
                     recommendation.PotentialSavings >= 100).Take(10))
        {
            anomalies.Add(new InsightAnomaly
            {
                Id = $"recommendation:{recommendation.Id}",
                Category = "Optimization",
                Severity = recommendation.PotentialSavings >= 500 ? "High" : "Medium",
                Title = $"{recommendation.RecommendationType} opportunity detected",
                Summary = $"{recommendation.Description} Potential savings: {recommendation.PotentialSavings} {recommendation.Currency}.",
                Provider = recommendation.Provider,
                SubscriptionId = recommendation.SubscriptionId,
                ResourceId = recommendation.ResourceId,
                DetectionMethod = "Recommendation ranking",
                CurrentValue = recommendation.PotentialSavings,
                Unit = recommendation.Currency
            });
        }

        return anomalies
            .OrderByDescending(item => SeverityScore(item.Severity))
            .ThenBy(item => item.Category)
            .Take(25)
            .ToList();
    }

    private static List<InsightWorkflowSuggestion> BuildWorkflowSuggestions(
        IReadOnlyCollection<InsightAnomaly> anomalies,
        IReadOnlyCollection<InsightRecommendation> recommendations)
    {
        var workflows = new List<InsightWorkflowSuggestion>();

        foreach (var anomaly in anomalies)
        {
            var action = anomaly.Category switch
            {
                "ConnectionHealth" => "queue-connection-resync",
                "Budget" => "trigger-budget-alert",
                "CostSpike" => "open-cost-investigation",
                "Metric" => "open-resource-diagnostics",
                "Optimization" => "queue-optimization-review",
                _ => "review-anomaly"
            };

            workflows.Add(new InsightWorkflowSuggestion
            {
                Id = $"workflow:{anomaly.Id}",
                Trigger = anomaly.Category,
                SuggestedAction = action,
                Severity = anomaly.Severity,
                Reason = anomaly.Summary,
                Provider = anomaly.Provider,
                SubscriptionId = anomaly.SubscriptionId,
                ResourceId = anomaly.ResourceId,
                CanAutoRun = false
            });
        }

        foreach (var recommendation in recommendations
                     .Where(item => item.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(item => item.PotentialSavings)
                     .Take(5))
        {
            workflows.Add(new InsightWorkflowSuggestion
            {
                Id = $"workflow:recommendation:{recommendation.Id}",
                Trigger = "Recommendation",
                SuggestedAction = "draft-optimization-plan",
                Severity = recommendation.PotentialSavings >= 500 ? "High" : "Medium",
                Reason = recommendation.Description,
                Provider = recommendation.Provider,
                SubscriptionId = recommendation.SubscriptionId,
                ResourceId = recommendation.ResourceId,
                CanAutoRun = false
            });
        }

        return workflows
            .GroupBy(item => item.Id)
            .Select(group => group.First())
            .OrderByDescending(item => SeverityScore(item.Severity))
            .Take(20)
            .ToList();
    }

    private static double? ResolveMetricThreshold(string metricName)
    {
        var normalized = metricName.Trim().ToLowerInvariant();
        return normalized switch
        {
            var value when value.Contains("cpu") => 80,
            var value when value.Contains("memory") => 85,
            var value when value.Contains("error") => 1,
            var value when value.Contains("latency") => 1000,
            _ => null
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

    private static decimal ForecastMonthlyCost(IReadOnlyCollection<CloudCostSnapshot> snapshots)
    {
        var now = DateTimeOffset.UtcNow;
        var currentMonthSnapshots = snapshots
            .Where(snapshot =>
                snapshot.PeriodEnd.Year == now.Year &&
                snapshot.PeriodEnd.Month == now.Month &&
                string.Equals(snapshot.Granularity, "Daily", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (currentMonthSnapshots.Count == 0)
        {
            return snapshots
                .GroupBy(snapshot => new { snapshot.Provider, snapshot.SubscriptionId, snapshot.Currency })
                .Select(group => group.OrderByDescending(item => item.PeriodEnd).First().Amount)
                .DefaultIfEmpty(0)
                .Sum();
        }

        var monthToDate = currentMonthSnapshots.Sum(snapshot => snapshot.Amount);
        var daysElapsed = Math.Max(1, now.Day);
        var daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);

        return Math.Round(monthToDate / daysElapsed * daysInMonth, 2);
    }
}
