using Draco.Domain.Entities;

namespace Draco.Application.Interfaces;

public interface ICostGovernanceService
{
    Task<IEnumerable<CostBudget>> GetBudgetsAsync(string? userPhone = null);
    Task<CostBudget> CreateBudgetAsync(CostBudget budget);
    Task<IEnumerable<CostRecommendation>> GetRecommendationsAsync(string provider);
    Task<decimal> GetCurrentSpendAsync(string provider, string subscriptionId);
    Task<decimal> ForecastMonthlySpendAsync(string provider, string subscriptionId);
    Task RunCostAnalysisAsync(CancellationToken cancellationToken = default);
}
