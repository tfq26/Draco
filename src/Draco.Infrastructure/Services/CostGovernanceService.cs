using Draco.Application.Interfaces;
using Draco.Domain.Entities;
using Draco.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Draco.Infrastructure.Services;

public class CostGovernanceService : ICostGovernanceService
{
    private readonly DracoDbContext _dbContext;
    private readonly IEnumerable<ICloudProvider> _providers;
    private readonly ILogger<CostGovernanceService> _logger;

    public CostGovernanceService(
        DracoDbContext dbContext,
        IEnumerable<ICloudProvider> providers,
        ILogger<CostGovernanceService> logger)
    {
        _dbContext = dbContext;
        _providers = providers;
        _logger = logger;
    }

    public async Task<IEnumerable<CostBudget>> GetBudgetsAsync(string? userPhone = null)
    {
        var budgets = _dbContext.CostBudgets.AsNoTracking();

        if (string.IsNullOrWhiteSpace(userPhone))
        {
            return await budgets.ToListAsync();
        }

        var userId = await _dbContext.UserAccounts
            .Where(user => user.Phone == userPhone)
            .Select(user => user.Id)
            .FirstOrDefaultAsync();

        return userId == Guid.Empty
            ? []
            : await budgets.Where(budget => budget.UserId == userId).ToListAsync();
    }

    public async Task<CostBudget> CreateBudgetAsync(CostBudget budget)
    {
        _dbContext.CostBudgets.Add(budget);
        await _dbContext.SaveChangesAsync();
        return budget;
    }

    public async Task<IEnumerable<CostRecommendation>> GetRecommendationsAsync(string provider)
    {
        return await _dbContext.CostRecommendations
            .Where(r => r.Provider == provider)
            .ToListAsync();
    }

    public async Task<decimal> GetCurrentSpendAsync(string provider, string subscriptionId)
    {
        var normalizedProvider = provider.Trim();

        return await _dbContext.CloudCostSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.Provider == normalizedProvider && snapshot.SubscriptionId == subscriptionId)
            .OrderByDescending(snapshot => snapshot.PeriodEnd)
            .Select(snapshot => snapshot.Amount)
            .FirstOrDefaultAsync();
    }

    public async Task<decimal> ForecastMonthlySpendAsync(string provider, string subscriptionId)
    {
        var now = DateTimeOffset.UtcNow;
        var dailySnapshots = await _dbContext.CloudCostSnapshots
            .AsNoTracking()
            .Where(snapshot =>
                snapshot.Provider == provider &&
                snapshot.SubscriptionId == subscriptionId &&
                snapshot.Granularity == "Daily" &&
                snapshot.PeriodEnd.Year == now.Year &&
                snapshot.PeriodEnd.Month == now.Month)
            .ToListAsync();

        if (dailySnapshots.Count == 0)
        {
            return await GetCurrentSpendAsync(provider, subscriptionId);
        }

        var monthToDate = dailySnapshots.Sum(snapshot => snapshot.Amount);
        var daysElapsed = Math.Max(1, now.Day);
        var daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
        return Math.Round(monthToDate / daysElapsed * daysInMonth, 2);
    }

    public async Task RunCostAnalysisAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting cross-cloud cost analysis...");
        
        foreach (var provider in _providers)
        {
            try
            {
                _logger.LogInformation("Analyzing costs for {Provider}", provider.ProviderName);
                // 1. Fetch current spend and compare with budgets
                // 2. Scan for unused/idle resources
                // 3. Upsert recommendations
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cost analysis for {Provider}", provider.ProviderName);
            }
        }

        await Task.CompletedTask;
    }
}
