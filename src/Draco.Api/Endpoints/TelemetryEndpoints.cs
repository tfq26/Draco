using System.Security.Claims;
using Draco.Application.Interfaces;
using Draco.Domain.Entities;
using Draco.Domain.Repositories;
using Draco.Infrastructure.Data;
using Draco.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Draco.Api.Endpoints;

public static class TelemetryEndpoints
{
    public static void MapTelemetryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api");

        group.MapPost("/ingest", IngestCloudSnapshotAsync)
            .RequireAuthorization()
            .WithName("IngestCloudSnapshot");

        group.MapPost("/telemetry/metrics", IngestMetricsAsync)
            .RequireAuthorization()
            .WithName("IngestMetrics");

        group.MapPost("/telemetry/logs", IngestLogsAsync)
            .RequireAuthorization()
            .WithName("IngestLogs");

        group.MapGet("/telemetry/metrics", GetMetricsAsync)
            .RequireAuthorization()
            .WithName("GetMetrics");
    }

    private static async Task<IResult> IngestCloudSnapshotAsync(
        [FromBody] CloudDataIngestionRequest request,
        ClaimsPrincipal userPrincipal,
        DracoDbContext dbContext,
        IResourceRepository resourceRepository,
        ITelemetryService telemetryService,
        CancellationToken cancellationToken)
    {
        var user = await userPrincipal.GetCurrentUserAsync(dbContext, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var provider = AuthEndpoints.NormalizeProvider(request.Provider);
        var connection = user.Connections.FirstOrDefault(existing =>
            existing.IsActive &&
            existing.Provider == provider &&
            existing.SubscriptionId == request.SubscriptionId);

        if (connection is null)
        {
            return Results.BadRequest(new { message = "No active cloud connection found for that provider and subscription." });
        }

        var resources = (request.Resources ?? [])
            .Where(resource => !string.IsNullOrWhiteSpace(resource.Id))
            .Select(resource => new CloudResource
            {
                Id = resource.Id.Trim(),
                Name = resource.Name.Trim(),
                Type = resource.Type.Trim(),
                Provider = provider,
                Location = resource.Location?.Trim() ?? string.Empty,
                SubscriptionId = request.SubscriptionId.Trim(),
                ResourceGroupName = !string.IsNullOrWhiteSpace(resource.ResourceGroupName)
                    ? resource.ResourceGroupName.Trim()
                    : ExtractResourceGroupName(resource.Id),
                Tags = resource.Tags ?? new Dictionary<string, string>(),
                DiscoveredAt = resource.DiscoveredAt ?? DateTimeOffset.UtcNow,
                RawMetadata = resource.RawMetadata
            })
            .ToList();

        if (resources.Count > 0)
        {
            await resourceRepository.UpsertResourcesAsync(resources, cancellationToken);
        }

        var existingSnapshots = await dbContext.CloudCostSnapshots
            .Where(snapshot =>
                snapshot.UserId == user.Id &&
                snapshot.Provider == provider &&
                snapshot.SubscriptionId == request.SubscriptionId)
            .ToListAsync(cancellationToken);

        foreach (var costSnapshot in request.CostSnapshots ?? [])
        {
            var periodStart = costSnapshot.PeriodStart ?? DateTimeOffset.UtcNow.Date;
            var periodEnd = costSnapshot.PeriodEnd ?? periodStart;

            dbContext.CloudCostSnapshots.RemoveRange(existingSnapshots.Where(existing =>
                existing.Granularity == (costSnapshot.Granularity ?? "Monthly") &&
                existing.PeriodStart == periodStart &&
                existing.PeriodEnd == periodEnd));

            dbContext.CloudCostSnapshots.Add(new CloudCostSnapshot
            {
                UserId = user.Id,
                Provider = provider,
                SubscriptionId = request.SubscriptionId.Trim(),
                Amount = costSnapshot.Amount,
                Currency = string.IsNullOrWhiteSpace(costSnapshot.Currency) ? "USD" : costSnapshot.Currency.Trim(),
                Granularity = string.IsNullOrWhiteSpace(costSnapshot.Granularity) ? "Monthly" : costSnapshot.Granularity.Trim(),
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                CapturedAt = costSnapshot.CapturedAt ?? DateTimeOffset.UtcNow,
                RawData = costSnapshot.RawData
            });
        }

        var existingRecommendations = await dbContext.CostRecommendations
            .Where(recommendation =>
                recommendation.Provider == provider &&
                recommendation.SubscriptionId == request.SubscriptionId)
            .ToListAsync(cancellationToken);

        if (existingRecommendations.Count > 0)
        {
            dbContext.CostRecommendations.RemoveRange(existingRecommendations);
        }

        dbContext.CostRecommendations.AddRange((request.Recommendations ?? []).Select(recommendation => new CostRecommendation
        {
            ResourceId = recommendation.ResourceId?.Trim() ?? string.Empty,
            ResourceName = recommendation.ResourceName?.Trim() ?? string.Empty,
            Provider = provider,
            SubscriptionId = request.SubscriptionId.Trim(),
            RecommendationType = recommendation.RecommendationType.Trim(),
            Description = recommendation.Description.Trim(),
            PotentialSavings = recommendation.PotentialSavings,
            Currency = string.IsNullOrWhiteSpace(recommendation.Currency) ? "USD" : recommendation.Currency.Trim(),
            Status = string.IsNullOrWhiteSpace(recommendation.Status) ? "Pending" : recommendation.Status.Trim(),
            DiscoveredAt = recommendation.DiscoveredAt ?? DateTimeOffset.UtcNow
        }));

        if ((request.Metrics?.Count ?? 0) > 0)
        {
            await telemetryService.IngestMetricsAsync(request.Metrics!);
        }

        if ((request.Logs?.Count ?? 0) > 0)
        {
            await telemetryService.IngestLogsAsync(request.Logs!);
        }

        connection.LastSyncedAt = DateTimeOffset.UtcNow;
        connection.SyncStatus = "Healthy";
        connection.SyncMessage = $"Imported {resources.Count} resources, {request.CostSnapshots?.Count ?? 0} cost snapshots, and {request.Recommendations?.Count ?? 0} recommendations.";

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new
        {
            message = "Cloud snapshot ingested successfully.",
            resources = resources.Count,
            costSnapshots = request.CostSnapshots?.Count ?? 0,
            recommendations = request.Recommendations?.Count ?? 0,
            metrics = request.Metrics?.Count ?? 0,
            logs = request.Logs?.Count ?? 0
        });
    }

    private static async Task<IResult> IngestMetricsAsync(
        [FromBody] List<ObservabilityMetric> metrics,
        ITelemetryService telemetryService)
    {
        await telemetryService.IngestMetricsAsync(metrics);
        return Results.Ok(new { count = metrics.Count });
    }

    private static async Task<IResult> IngestLogsAsync(
        [FromBody] List<ObservabilityLog> logs,
        ITelemetryService telemetryService)
    {
        await telemetryService.IngestLogsAsync(logs);
        return Results.Ok(new { count = logs.Count });
    }

    private static async Task<IResult> GetMetricsAsync(
        ClaimsPrincipal userPrincipal,
        string resourceId,
        string metricName,
        DateTimeOffset? start,
        DateTimeOffset? end,
        DracoDbContext dbContext,
        ITelemetryService telemetryService,
        CancellationToken cancellationToken)
    {
        var user = await userPrincipal.GetCurrentUserAsync(dbContext, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var resourceBelongsToUser = await dbContext.CloudResources
            .AnyAsync(resource =>
                resource.Id == resourceId &&
                user.Connections.Select(connection => connection.SubscriptionId).Contains(resource.SubscriptionId),
                cancellationToken);

        if (!resourceBelongsToUser)
        {
            return Results.NotFound(new { message = "Resource not found for current user." });
        }

        var metrics = await telemetryService.GetMetricsAsync(
            resourceId,
            metricName,
            start ?? DateTimeOffset.UtcNow.AddHours(-24),
            end ?? DateTimeOffset.UtcNow);

        return Results.Ok(metrics);
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
}

public sealed record CloudDataIngestionRequest(
    string Provider,
    string SubscriptionId,
    List<IngestedCloudResource>? Resources,
    List<IngestedCostSnapshot>? CostSnapshots,
    List<IngestedCostRecommendation>? Recommendations,
    List<ObservabilityMetric>? Metrics,
    List<ObservabilityLog>? Logs);

public sealed record IngestedCloudResource(
    string Id,
    string Name,
    string Type,
    string? Location,
    string? ResourceGroupName,
    Dictionary<string, string>? Tags,
    DateTimeOffset? DiscoveredAt,
    string? RawMetadata);

public sealed record IngestedCostSnapshot(
    decimal Amount,
    string? Currency,
    string? Granularity,
    DateTimeOffset? PeriodStart,
    DateTimeOffset? PeriodEnd,
    DateTimeOffset? CapturedAt,
    string? RawData);

public sealed record IngestedCostRecommendation(
    string? ResourceId,
    string? ResourceName,
    string RecommendationType,
    string Description,
    decimal PotentialSavings,
    string? Currency,
    string? Status,
    DateTimeOffset? DiscoveredAt);
