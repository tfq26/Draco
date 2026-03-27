using System.Security.Claims;
using Draco.Application.Interfaces;
using Draco.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Draco.Api.Endpoints;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", GetHealthAsync)
            .WithName("HealthCheck");

        app.MapGet("/api/dashboard/summary", GetDashboardSummaryAsync)
            .RequireAuthorization()
            .WithName("GetDashboardSummary");

        app.MapGet("/api/monitoring/stats", GetMonitoringStatsAsync)
            .RequireAuthorization()
            .WithName("GetMonitoringStats");

        app.MapGet("/api/monitoring/alerts", GetMonitoringAlertsAsync)
            .RequireAuthorization()
            .WithName("GetMonitoringAlerts");

        app.MapGet("/api/costs/overview", GetCostOverviewAsync)
            .RequireAuthorization()
            .WithName("GetCostOverview");

        app.MapGet("/api/costs/budgets", GetBudgetsAsync)
            .RequireAuthorization()
            .WithName("GetBudgets");

        app.MapPost("/api/costs/budgets", CreateBudgetAsync)
            .RequireAuthorization()
            .WithName("CreateBudget");

        app.MapGet("/api/recommendations", GetRecommendationsAsync)
            .RequireAuthorization()
            .WithName("GetRecommendations");

        app.MapGet("/api/governance/policies", GetGovernancePoliciesAsync)
            .RequireAuthorization()
            .WithName("GetGovernancePolicies");

        app.MapGet("/api/ai/context", GetPreparedAiContextAsync)
            .RequireAuthorization()
            .WithName("GetPreparedAiContext");

        app.MapGet("/api/workflows/suggestions", GetWorkflowSuggestionsAsync)
            .RequireAuthorization()
            .WithName("GetWorkflowSuggestions");

        app.MapPost("/api/ai/query", QueryInsightsAsync)
            .RequireAuthorization()
            .WithName("QueryInsights");
    }

    public static Task<IResult> GetHealthAsync(DracoDbContext dbContext, CancellationToken cancellationToken) =>
        GetHealthResultAsync(dbContext, cancellationToken);

    public static async Task<IResult> GetDashboardSummaryAsync(
        ClaimsPrincipal userPrincipal,
        DracoDbContext dbContext,
        IInsightContextService insightContextService,
        CancellationToken cancellationToken)
    {
        var user = await userPrincipal.GetCurrentUserAsync(dbContext, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var context = await insightContextService.BuildForUserAsync(user.Id, cancellationToken);
        return context is null
            ? Results.NotFound(new { message = "Insight context not available." })
            : Results.Ok(context);
    }

    public static async Task<IResult> GetMonitoringStatsAsync(
        ClaimsPrincipal userPrincipal,
        DracoDbContext dbContext,
        IInsightContextService insightContextService,
        CancellationToken cancellationToken)
    {
        var context = await ResolveInsightContextAsync(userPrincipal, dbContext, insightContextService, cancellationToken);
        if (context is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(new
        {
            resources = context.Overview.ResourceCount,
            providers = context.Overview.ProviderCount,
            monthlyCost = context.Overview.CurrentMonthlyCost,
            forecastMonthlyCost = context.Overview.ForecastMonthlyCost,
            potentialSavings = context.Overview.PotentialMonthlySavings,
            activeAlerts = context.Overview.OpenAlertCount,
            anomalies = context.Overview.AnomalyCount,
            healthyConnections = context.Connections.Count(connection => string.Equals(connection.SyncStatus, "Healthy", StringComparison.OrdinalIgnoreCase)),
            lastSyncedAt = context.Overview.LastSyncedAt
        });
    }

    public static async Task<IResult> GetMonitoringAlertsAsync(
        ClaimsPrincipal userPrincipal,
        DracoDbContext dbContext,
        IInsightContextService insightContextService,
        CancellationToken cancellationToken)
    {
        var context = await ResolveInsightContextAsync(userPrincipal, dbContext, insightContextService, cancellationToken);
        if (context is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(context.Anomalies);
    }

    public static async Task<IResult> GetCostOverviewAsync(
        ClaimsPrincipal userPrincipal,
        DracoDbContext dbContext,
        IInsightContextService insightContextService,
        CancellationToken cancellationToken)
    {
        var context = await ResolveInsightContextAsync(userPrincipal, dbContext, insightContextService, cancellationToken);
        if (context is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(new
        {
            totalMonthlyCost = context.Overview.CurrentMonthlyCost,
            forecastMonthlyCost = context.Overview.ForecastMonthlyCost,
            potentialSavings = context.Overview.PotentialMonthlySavings,
            budgets = context.Budgets,
            breakdown = context.CostBreakdown
        });
    }

    public static async Task<IResult> GetBudgetsAsync(
        ClaimsPrincipal userPrincipal,
        DracoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var user = await userPrincipal.GetCurrentUserAsync(dbContext, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var budgets = await dbContext.CostBudgets
            .AsNoTracking()
            .Where(budget => budget.UserId == user.Id)
            .OrderByDescending(budget => budget.CreatedAt)
            .ToListAsync(cancellationToken);

        return Results.Ok(budgets);
    }

    public static async Task<IResult> CreateBudgetAsync(
        [FromBody] CreateBudgetRequest request,
        ClaimsPrincipal userPrincipal,
        DracoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var user = await userPrincipal.GetCurrentUserAsync(dbContext, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Name) ||
            string.IsNullOrWhiteSpace(request.Provider) ||
            string.IsNullOrWhiteSpace(request.SubscriptionId))
        {
            return Results.BadRequest(new { message = "Name, provider, and subscription ID are required." });
        }

        var budget = new Draco.Domain.Entities.CostBudget
        {
            UserId = user.Id,
            Name = request.Name.Trim(),
            Provider = AuthEndpoints.NormalizeProvider(request.Provider),
            SubscriptionId = request.SubscriptionId.Trim(),
            BudgetSource = "Manual",
            Scope = request.SubscriptionId.Trim(),
            ScopeType = "Subscription",
            ScopeDisplayName = request.SubscriptionId.Trim(),
            Amount = request.Amount,
            Currency = string.IsNullOrWhiteSpace(request.Currency) ? "USD" : request.Currency.Trim(),
            TimeGrain = string.IsNullOrWhiteSpace(request.TimeGrain) ? "Monthly" : request.TimeGrain.Trim(),
            AlertThresholdPercentage = request.AlertThresholdPercentage <= 0 ? 80 : request.AlertThresholdPercentage,
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = request.IsActive
        };

        dbContext.CostBudgets.Add(budget);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(budget);
    }

    public static async Task<IResult> GetRecommendationsAsync(
        ClaimsPrincipal userPrincipal,
        DracoDbContext dbContext,
        IInsightContextService insightContextService,
        string? provider,
        CancellationToken cancellationToken)
    {
        var context = await ResolveInsightContextAsync(userPrincipal, dbContext, insightContextService, cancellationToken);
        if (context is null)
        {
            return Results.Unauthorized();
        }

        var recommendations = context.Recommendations.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(provider))
        {
            var normalizedProvider = AuthEndpoints.NormalizeProvider(provider);
            recommendations = recommendations.Where(recommendation => recommendation.Provider == normalizedProvider);
        }

        return Results.Ok(recommendations);
    }

    public static async Task<IResult> GetGovernancePoliciesAsync(
        ClaimsPrincipal userPrincipal,
        DracoDbContext dbContext,
        IInsightContextService insightContextService,
        CancellationToken cancellationToken)
    {
        var context = await ResolveInsightContextAsync(userPrincipal, dbContext, insightContextService, cancellationToken);
        if (context is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(context.Budgets.Select(budget => new
        {
            id = budget.BudgetId,
            name = budget.Name,
            provider = budget.Provider,
            subscriptionId = budget.SubscriptionId,
            type = "budget",
            threshold = budget.AlertThresholdPercentage,
            limit = budget.LimitAmount,
            current = budget.CurrentAmount,
            currency = budget.Currency,
            status = budget.Status
        }));
    }

    public static async Task<IResult> GetPreparedAiContextAsync(
        ClaimsPrincipal userPrincipal,
        DracoDbContext dbContext,
        IInsightContextService insightContextService,
        CancellationToken cancellationToken)
    {
        var context = await ResolveInsightContextAsync(userPrincipal, dbContext, insightContextService, cancellationToken);
        return context is null
            ? Results.Unauthorized()
            : Results.Ok(new
            {
                context,
                modelContext = insightContextService.SerializeForModel(context)
            });
    }

    public static async Task<IResult> GetWorkflowSuggestionsAsync(
        ClaimsPrincipal userPrincipal,
        DracoDbContext dbContext,
        IInsightContextService insightContextService,
        CancellationToken cancellationToken)
    {
        var context = await ResolveInsightContextAsync(userPrincipal, dbContext, insightContextService, cancellationToken);
        return context is null
            ? Results.Unauthorized()
            : Results.Ok(context.WorkflowSuggestions);
    }

    public static async Task<IResult> QueryInsightsAsync(
        [FromBody] AiQueryRequest request,
        ClaimsPrincipal userPrincipal,
        DracoDbContext dbContext,
        IAIService aiService,
        IInsightContextService insightContextService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return Results.BadRequest(new { message = "Query is required." });
        }

        var context = await ResolveInsightContextAsync(userPrincipal, dbContext, insightContextService, cancellationToken);
        if (context is null)
        {
            return Results.Unauthorized();
        }

        var answer = await aiService.ProcessQueryAsync(
            request.Query.Trim(),
            insightContextService.SerializeForModel(context),
            cancellationToken);

        return Results.Ok(new
        {
            answer,
            contextSummary = context.Overview,
            workflowSuggestions = context.WorkflowSuggestions.Take(5)
        });
    }

    private static async Task<IResult> GetHealthResultAsync(DracoDbContext dbContext, CancellationToken cancellationToken)
    {
        var resourceCount = await dbContext.CloudResources.CountAsync(cancellationToken);
        var userCount = await dbContext.UserAccounts.CountAsync(cancellationToken);
        var connectionCount = await dbContext.CloudConnections.CountAsync(cancellationToken);

        return Results.Ok(new
        {
            status = "Healthy",
            timestamp = DateTimeOffset.UtcNow,
            resources = resourceCount,
            users = userCount,
            connections = connectionCount
        });
    }

    private static async Task<Draco.Application.Models.PreparedInsightContext?> ResolveInsightContextAsync(
        ClaimsPrincipal userPrincipal,
        DracoDbContext dbContext,
        IInsightContextService insightContextService,
        CancellationToken cancellationToken)
    {
        var user = await userPrincipal.GetCurrentUserAsync(dbContext, cancellationToken);
        return user is null
            ? null
            : await insightContextService.BuildForUserAsync(user.Id, cancellationToken);
    }
}

public sealed record CreateBudgetRequest(
    string Name,
    string Provider,
    string SubscriptionId,
    decimal Amount,
    string? Currency,
    string? TimeGrain,
    double AlertThresholdPercentage,
    bool IsActive);

public sealed record AiQueryRequest(string Query);
