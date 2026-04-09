using Draco.Application.Models;
using Draco.Domain.Entities;

namespace Draco.Infrastructure.Services;

public sealed record ScopeCurrentSpend(
    string Provider,
    string SubscriptionId,
    string Currency,
    decimal CurrentAmount,
    string Source);

public static class CurrentSpendSummaryBuilder
{
    public static IReadOnlyDictionary<string, ScopeCurrentSpend> BuildCurrentSpendByScope(
        IReadOnlyCollection<CostBudget> budgets,
        IReadOnlyCollection<CloudCostSnapshot> costSnapshots,
        IReadOnlyCollection<CloudResourceCost> latestPeriodResourceCosts)
    {
        var spendMap = new Dictionary<string, ScopeCurrentSpend>(StringComparer.OrdinalIgnoreCase);

        foreach (var snapshot in costSnapshots
                     .GroupBy(snapshot => $"{snapshot.Provider}:{snapshot.SubscriptionId}", StringComparer.OrdinalIgnoreCase)
                     .Select(group => group
                         .OrderByDescending(snapshot => snapshot.PeriodStart)
                         .ThenByDescending(snapshot => snapshot.PeriodEnd)
                         .First()))
        {
            spendMap[BuildScopeKey(snapshot.Provider, snapshot.SubscriptionId)] = new ScopeCurrentSpend(
                snapshot.Provider,
                snapshot.SubscriptionId,
                snapshot.Currency,
                snapshot.Amount,
                "Snapshot");
        }

        foreach (var rollup in latestPeriodResourceCosts
                     .GroupBy(cost => $"{cost.Provider}:{cost.SubscriptionId}", StringComparer.OrdinalIgnoreCase)
                     .Select(group => new ScopeCurrentSpend(
                         group.First().Provider,
                         group.First().SubscriptionId,
                         group.First().Currency,
                         group.Sum(item => item.Amount),
                         "ResourceRollup")))
        {
            var key = BuildScopeKey(rollup.Provider, rollup.SubscriptionId);
            if (!spendMap.ContainsKey(key))
            {
                spendMap[key] = rollup;
            }
        }

        foreach (var importedBudget in budgets
                     .Where(budget =>
                         !string.Equals(budget.BudgetSource, "Manual", StringComparison.OrdinalIgnoreCase) &&
                         budget.CurrentSpend.HasValue)
                     .GroupBy(budget => BuildScopeKey(budget.Provider, budget.SubscriptionId), StringComparer.OrdinalIgnoreCase)
                     .Select(group => group
                         .OrderByDescending(budget => string.Equals(budget.ScopeType, "Subscription", StringComparison.OrdinalIgnoreCase))
                         .ThenByDescending(budget => budget.LastSyncedAt ?? budget.CreatedAt)
                         .First()))
        {
            var key = BuildScopeKey(importedBudget.Provider, importedBudget.SubscriptionId);
            spendMap[key] = new ScopeCurrentSpend(
                importedBudget.Provider,
                importedBudget.SubscriptionId,
                importedBudget.Currency,
                importedBudget.CurrentSpend!.Value,
                "ImportedBudget");
        }

        return spendMap;
    }

    public static List<InsightProviderCostBreakdown> BuildProviderCostBreakdown(
        IReadOnlyCollection<ScopeCurrentSpend> currentSpendByScope,
        IReadOnlyDictionary<string, int> resourceCountsByProvider)
    {
        return currentSpendByScope
            .GroupBy(spend => new { spend.Provider, spend.Currency })
            .Select(group => new InsightProviderCostBreakdown
            {
                Provider = group.Key.Provider,
                Currency = group.Key.Currency,
                TotalAmount = group.Sum(item => item.CurrentAmount),
                ResourceCount = resourceCountsByProvider.GetValueOrDefault(group.Key.Provider, 0)
            })
            .OrderByDescending(item => item.TotalAmount)
            .ToList();
    }

    public static List<CloudResourceCost> SelectLatestPeriodRollupCosts(IReadOnlyCollection<CloudResourceCost> resourceCosts) =>
        resourceCosts
            .GroupBy(cost => BuildScopeKey(cost.Provider, cost.SubscriptionId), StringComparer.OrdinalIgnoreCase)
            .SelectMany(group =>
            {
                var latestPeriodStart = group.Max(cost => cost.PeriodStart);
                var latestPeriodCosts = group
                    .Where(cost => cost.PeriodStart == latestPeriodStart)
                    .ToList();

                return SelectPreferredRollupCosts(latestPeriodCosts);
            })
            .ToList();

    public static List<CloudResourceCost> SelectPreferredRollupCosts(IReadOnlyCollection<CloudResourceCost> resourceCosts)
    {
        var actualCosts = resourceCosts
            .Where(IsActualCostSource)
            .ToList();

        return actualCosts.Count > 0 ? actualCosts : [.. resourceCosts];
    }

    public static List<CloudResourceCost> SelectPreferredMonthlyResourceCosts(IReadOnlyCollection<CloudResourceCost> resourceCosts)
    {
        return resourceCosts
            .GroupBy(cost => cost.PeriodStart)
            .Select(group =>
            {
                var preferredGroup = SelectPreferredRollupCosts(group.ToList());
                return preferredGroup
                    .OrderByDescending(cost => cost.CapturedAt)
                    .First();
            })
            .OrderByDescending(cost => cost.PeriodStart)
            .ThenByDescending(cost => cost.CapturedAt)
            .ToList();
    }

    public static bool IsActualCostSource(CloudResourceCost cost) =>
        string.Equals(cost.CostSource, "AzureActual", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(cost.CostSource, "AwsActual", StringComparison.OrdinalIgnoreCase);

    private static string BuildScopeKey(string provider, string subscriptionId) => $"{provider}:{subscriptionId}";
}
