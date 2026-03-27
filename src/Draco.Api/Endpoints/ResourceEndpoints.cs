using System.Security.Claims;
using Draco.Application.Interfaces;
using Draco.Domain.Entities;
using Draco.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Draco.Api.Endpoints;

public static class ResourceEndpoints
{
    public static void MapResourceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/resources").RequireAuthorization();

        group.MapGet("/list", ListResourcesAsync)
            .WithName("ListResources");

        group.MapGet("/detail", GetResourceAsync)
            .WithName("GetResourceByQuery");

        group.MapPost("/actions/execute", ExecuteResourceActionAsync)
            .WithName("ExecuteResourceAction");

        group.MapGet("/{**id}", GetResourceAsync)
            .WithName("GetResource");
    }

    private static async Task<IResult> ListResourcesAsync(
        ClaimsPrincipal userPrincipal,
        DracoDbContext dbContext,
        string? provider,
        string? subscriptionId,
        string? search,
        CancellationToken cancellationToken)
    {
        var user = await userPrincipal.GetCurrentUserAsync(dbContext, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var allowedSubscriptions = user.Connections
            .Where(connection => connection.IsActive)
            .Select(connection => connection.SubscriptionId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct()
            .ToList();

        var query = dbContext.CloudResources
            .AsNoTracking()
            .Where(resource => allowedSubscriptions.Contains(resource.SubscriptionId));

        if (!string.IsNullOrWhiteSpace(provider))
        {
            var normalizedProvider = AuthEndpoints.NormalizeProvider(provider);
            query = query.Where(resource => resource.Provider == normalizedProvider);
        }

        if (!string.IsNullOrWhiteSpace(subscriptionId))
        {
            query = query.Where(resource => resource.SubscriptionId == subscriptionId);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTerm = search.Trim();
            query = query.Where(resource =>
                resource.Name.Contains(searchTerm) ||
                resource.Type.Contains(searchTerm) ||
                resource.Location.Contains(searchTerm));
        }

        var resources = await query
            .OrderBy(resource => resource.Provider)
            .ThenBy(resource => resource.Name)
            .ToListAsync(cancellationToken);

        var latestCosts = await dbContext.CloudResourceCosts
            .AsNoTracking()
            .Where(cost =>
                cost.UserId == user.Id &&
                allowedSubscriptions.Contains(cost.SubscriptionId))
            .OrderByDescending(cost => cost.PeriodEnd)
            .ThenByDescending(cost => cost.CapturedAt)
            .ToListAsync(cancellationToken);

        var costMap = latestCosts
            .GroupBy(cost => cost.ResourceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        return Results.Ok(resources.Select(resource =>
        {
            costMap.TryGetValue(resource.Id, out var cost);
            var resolvedResourceGroupName = !string.IsNullOrWhiteSpace(resource.ResourceGroupName)
                ? resource.ResourceGroupName
                : ExtractResourceGroupName(resource.Id) switch
                {
                    { Length: > 0 } resourceGroupNameFromId => resourceGroupNameFromId,
                    _ => cost?.ResourceGroupName ?? string.Empty
                };

            return new
            {
                id = resource.Id,
                name = resource.Name,
                type = resource.Type,
                provider = resource.Provider,
                location = resource.Location,
                subscriptionId = resource.SubscriptionId,
                resourceGroupName = resolvedResourceGroupName,
                tags = resource.Tags,
                discoveredAt = resource.DiscoveredAt,
                monthlyCost = cost?.Amount ?? 0m,
                currency = cost?.Currency ?? "USD",
                costSource = cost?.CostSource ?? "Unavailable",
                costCapturedAt = cost?.CapturedAt
            };
        }));
    }

    private static async Task<IResult> GetResourceAsync(
        string? id,
        ClaimsPrincipal userPrincipal,
        DracoDbContext dbContext,
        IResourceActionService resourceActionService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Results.BadRequest(new { message = "Resource id is required." });
        }

        var user = await userPrincipal.GetCurrentUserAsync(dbContext, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var allowedSubscriptions = user.Connections
            .Where(connection => connection.IsActive)
            .Select(connection => connection.SubscriptionId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct()
            .ToList();

        var resource = await dbContext.CloudResources
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.Id == id && allowedSubscriptions.Contains(candidate.SubscriptionId),
                cancellationToken);

        if (resource is null)
        {
            return Results.NotFound(new { message = "Resource not found." });
        }

        var recommendations = await dbContext.CostRecommendations
            .AsNoTracking()
            .Where(recommendation => recommendation.ResourceId == resource.Id)
            .OrderByDescending(recommendation => recommendation.PotentialSavings)
            .ToListAsync(cancellationToken);

        var recentMetrics = await dbContext.ObservabilityMetrics
            .AsNoTracking()
            .Where(metric => metric.ResourceId == resource.Id)
            .OrderByDescending(metric => metric.Timestamp)
            .Take(25)
            .ToListAsync(cancellationToken);

        var latestCost = await dbContext.CloudResourceCosts
            .AsNoTracking()
            .Where(cost => cost.UserId == user.Id && cost.ResourceId == resource.Id)
            .OrderByDescending(cost => cost.PeriodEnd)
            .ThenByDescending(cost => cost.CapturedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var resolvedResourceGroupName = !string.IsNullOrWhiteSpace(resource.ResourceGroupName)
            ? resource.ResourceGroupName
            : ExtractResourceGroupName(resource.Id) switch
            {
                { Length: > 0 } resourceGroupNameFromId => resourceGroupNameFromId,
                _ => latestCost?.ResourceGroupName ?? string.Empty
            };

        var periodCosts = latestCost is null
            ? []
            : await dbContext.CloudResourceCosts
                .AsNoTracking()
                .Where(cost =>
                    cost.UserId == user.Id &&
                    cost.Provider == latestCost.Provider &&
                    cost.SubscriptionId == latestCost.SubscriptionId &&
                    cost.PeriodStart == latestCost.PeriodStart)
                .ToListAsync(cancellationToken);

        var preferredPeriodCosts = SelectPreferredRollupCosts(periodCosts);

        var resourceGroupTotal = latestCost is null
            ? 0m
            : preferredPeriodCosts
                .Where(cost => string.Equals(cost.ResourceGroupName, resolvedResourceGroupName, StringComparison.OrdinalIgnoreCase))
                .Sum(cost => cost.Amount);

        var providerTotal = latestCost is null
            ? 0m
            : preferredPeriodCosts.Sum(cost => cost.Amount);

        var availableActions = await resourceActionService.GetSupportedActionsAsync(resource, cancellationToken);
        var actionAudits = await dbContext.RemediationAudits
            .AsNoTracking()
            .Where(audit => audit.ResourceId == resource.Id)
            .OrderByDescending(audit => audit.CreatedAt)
            .Take(10)
            .ToListAsync(cancellationToken);

        return Results.Ok(new
        {
            resource = new
            {
                resource.Id,
                resource.Name,
                resource.Type,
                resource.Provider,
                resource.Location,
                resource.SubscriptionId,
                resourceGroupName = resolvedResourceGroupName,
                resource.Tags,
                resource.DiscoveredAt
            },
            cost = latestCost,
            costContext = latestCost is null
                ? null
                : new
                {
                    providerTotal,
                    resourceGroupTotal
                },
            availableActions,
            actionAudits = actionAudits.Select(audit => new
            {
                audit.Id,
                audit.ActionType,
                audit.Status,
                audit.Description,
                audit.ErrorMessage,
                audit.CreatedAt,
                audit.CompletedAt
            }),
            recommendations,
            metrics = recentMetrics
        });
    }

    private static async Task<IResult> ExecuteResourceActionAsync(
        ExecuteResourceActionRequest request,
        ClaimsPrincipal userPrincipal,
        DracoDbContext dbContext,
        IResourceActionService resourceActionService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ResourceId) || string.IsNullOrWhiteSpace(request.Action))
        {
            return Results.BadRequest(new { message = "Resource id and action are required." });
        }

        var user = await userPrincipal.GetCurrentUserAsync(dbContext, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var allowedSubscriptions = user.Connections
            .Where(connection => connection.IsActive)
            .Select(connection => connection.SubscriptionId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct()
            .ToList();

        var resource = await dbContext.CloudResources
            .FirstOrDefaultAsync(candidate =>
                candidate.Id == request.ResourceId &&
                allowedSubscriptions.Contains(candidate.SubscriptionId),
                cancellationToken);

        if (resource is null)
        {
            return Results.NotFound(new { message = "Resource not found." });
        }

        var connection = user.Connections.FirstOrDefault(candidate =>
            candidate.IsActive &&
            string.Equals(candidate.Provider, resource.Provider, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.SubscriptionId, resource.SubscriptionId, StringComparison.OrdinalIgnoreCase));

        if (connection is null)
        {
            return Results.BadRequest(new { message = "No active cloud connection is available for this resource." });
        }

        try
        {
            var execution = await resourceActionService.ExecuteAsync(user, connection, resource, request.Action, cancellationToken);
            return Results.Ok(execution);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static string ExtractResourceGroupName(string? resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return string.Empty;
        }

        var segments = resourceId.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (segments[index].Equals("resourceGroups", StringComparison.OrdinalIgnoreCase))
            {
                return segments[index + 1];
            }
        }

        return string.Empty;
    }

    private static List<CloudResourceCost> SelectPreferredRollupCosts(IReadOnlyCollection<CloudResourceCost> resourceCosts)
    {
        var actualCosts = resourceCosts
            .Where(cost =>
                string.Equals(cost.CostSource, "AzureActual", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cost.CostSource, "AwsActual", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return actualCosts.Count > 0 ? actualCosts : [.. resourceCosts];
    }
}

public sealed record ExecuteResourceActionRequest(string ResourceId, string Action);
