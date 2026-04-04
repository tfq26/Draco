using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Amazon;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using Azure.Core;
using Azure.ResourceManager;
using Draco.Application.Interfaces;
using Draco.Application.Models;
using Draco.Domain.Entities;
using Draco.Domain.Repositories;
using Draco.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Draco.Infrastructure.Services;

public sealed class CloudConnectionSyncService : ICloudConnectionSyncService
{
    private readonly DracoDbContext _dbContext;
    private readonly IEnumerable<ICloudProvider> _providers;
    private readonly IEnumerable<INotificationRule> _notificationRules;
    private readonly INotificationEvaluationService _notificationEvaluationService;
    private readonly IResourceRepository _resourceRepository;
    private readonly IConfiguration _configuration;
    private readonly ILoggerFactory _loggerFactory;

    public CloudConnectionSyncService(
        DracoDbContext dbContext,
        IEnumerable<ICloudProvider> providers,
        IEnumerable<INotificationRule> notificationRules,
        INotificationEvaluationService notificationEvaluationService,
        IResourceRepository resourceRepository,
        IConfiguration configuration,
        ILoggerFactory loggerFactory)
    {
        _dbContext = dbContext;
        _providers = providers;
        _notificationRules = notificationRules;
        _notificationEvaluationService = notificationEvaluationService;
        _resourceRepository = resourceRepository;
        _configuration = configuration;
        _loggerFactory = loggerFactory;
    }

    public async Task<CloudConnectionSyncResult> SyncUserConnectionsAsync(
        UserAccount user,
        IReadOnlyCollection<int>? connectionIds = null,
        CancellationToken cancellationToken = default)
    {
        var providerMap = _providers.ToDictionary(
            provider => NormalizeProvider(provider.ProviderName),
            StringComparer.OrdinalIgnoreCase);

        var requestedIds = connectionIds?.ToHashSet();
        var targetConnections = user.Connections
            .Where(connection => connection.IsActive)
            .Where(connection => requestedIds is null || requestedIds.Contains(connection.Id))
            .ToList();

        var syncResults = new List<CloudConnectionSyncOutcome>();

        foreach (var connection in targetConnections)
        {
            if (!providerMap.TryGetValue(connection.Provider, out var provider))
            {
                connection.SyncStatus = "Unsupported";
                connection.SyncMessage = $"Provider '{connection.Provider}' is not configured on the API.";
                syncResults.Add(new CloudConnectionSyncOutcome
                {
                    ConnectionId = connection.Id,
                    Provider = connection.Provider,
                    SubscriptionId = connection.SubscriptionId,
                    Success = false,
                    Message = connection.SyncMessage
                });
                continue;
            }

            try
            {
                var accessToken = await ResolveProviderAccessTokenAsync(
                    connection,
                    cancellationToken);

                var resources = (await provider.ListResourcesAsync(accessToken, cancellationToken))
                    .Where(resource =>
                        string.IsNullOrWhiteSpace(connection.SubscriptionId) ||
                        string.IsNullOrWhiteSpace(resource.SubscriptionId) ||
                        string.Equals(resource.SubscriptionId, connection.SubscriptionId, StringComparison.OrdinalIgnoreCase))
                    .Select(resource =>
                    {
                        resource.Provider = connection.Provider;
                        resource.SubscriptionId = string.IsNullOrWhiteSpace(resource.SubscriptionId)
                            ? connection.SubscriptionId
                            : resource.SubscriptionId;
                        if (string.IsNullOrWhiteSpace(resource.ResourceGroupName))
                        {
                            resource.ResourceGroupName = ExtractResourceGroupName(resource.Id);
                        }

                        return resource;
                    })
                    .ToList();

                if (resources.Count > 0)
                {
                    await _resourceRepository.UpsertResourcesAsync(resources, cancellationToken);
                }

                var providerBudgets = (await provider.GetBudgetsAsync(
                        connection.SubscriptionId,
                        accessToken,
                        cancellationToken))
                    .ToList();

                var existingImportedBudgets = await _dbContext.CostBudgets
                    .Where(budget =>
                        budget.UserId == user.Id &&
                        budget.Provider == connection.Provider &&
                        budget.BudgetSource != "Manual")
                    .ToListAsync(cancellationToken);

                var importedBudgetKeys = providerBudgets
                    .Select(budget => budget.ExternalBudgetId)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var staleBudget in existingImportedBudgets.Where(budget =>
                             (string.Equals(budget.SubscriptionId, connection.SubscriptionId, StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(budget.ScopeType, "BillingAccount", StringComparison.OrdinalIgnoreCase)) &&
                             (string.IsNullOrWhiteSpace(budget.ExternalBudgetId) ||
                              !importedBudgetKeys.Contains(budget.ExternalBudgetId))))
                {
                    _dbContext.CostBudgets.Remove(staleBudget);
                }

                foreach (var importedBudget in providerBudgets)
                {
                    var existingBudget = existingImportedBudgets.FirstOrDefault(budget =>
                        string.Equals(budget.ExternalBudgetId, importedBudget.ExternalBudgetId, StringComparison.OrdinalIgnoreCase));

                    if (existingBudget is null)
                    {
                        _dbContext.CostBudgets.Add(new CostBudget
                        {
                            UserId = user.Id,
                            Name = importedBudget.Name,
                            Provider = connection.Provider,
                            SubscriptionId = connection.SubscriptionId,
                            BudgetSource = importedBudget.Source,
                            ExternalBudgetId = importedBudget.ExternalBudgetId,
                            Scope = importedBudget.Scope,
                            ScopeType = importedBudget.ScopeType,
                            ScopeDisplayName = importedBudget.ScopeDisplayName,
                            Amount = importedBudget.Amount,
                            CurrentSpend = importedBudget.CurrentSpend,
                            ForecastSpend = importedBudget.ForecastSpend,
                            Currency = importedBudget.Currency,
                            TimeGrain = importedBudget.TimeGrain,
                            AlertThresholdPercentage = importedBudget.AlertThresholdPercentage ?? 80,
                            NotificationSettingsJson = importedBudget.Notifications.Count == 0
                                ? null
                                : JsonSerializer.Serialize(importedBudget.Notifications),
                            CreatedAt = DateTimeOffset.UtcNow,
                            LastSyncedAt = DateTimeOffset.UtcNow,
                            IsActive = true
                        });

                        continue;
                    }

                    existingBudget.Name = importedBudget.Name;
                    existingBudget.Scope = importedBudget.Scope;
                    existingBudget.ScopeType = importedBudget.ScopeType;
                    existingBudget.ScopeDisplayName = importedBudget.ScopeDisplayName;
                    existingBudget.Amount = importedBudget.Amount;
                    existingBudget.CurrentSpend = importedBudget.CurrentSpend;
                    existingBudget.ForecastSpend = importedBudget.ForecastSpend;
                    existingBudget.Currency = importedBudget.Currency;
                    existingBudget.TimeGrain = importedBudget.TimeGrain;
                    existingBudget.AlertThresholdPercentage = importedBudget.AlertThresholdPercentage ?? existingBudget.AlertThresholdPercentage;
                    existingBudget.NotificationSettingsJson = importedBudget.Notifications.Count == 0
                        ? existingBudget.NotificationSettingsJson
                        : JsonSerializer.Serialize(importedBudget.Notifications);
                    existingBudget.LastSyncedAt = DateTimeOffset.UtcNow;
                    existingBudget.IsActive = true;
                }

                var metricsToPersist = new List<ObservabilityMetric>();
                foreach (var resource in resources)
                {
                    var requestedMetricNames = _notificationRules
                        .SelectMany(rule => rule.GetRequiredMetricNames(resource))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (requestedMetricNames.Count == 0)
                    {
                        continue;
                    }

                    var providerMetrics = (await provider.GetMetricsAsync(
                            resource,
                            requestedMetricNames,
                            TimeSpan.FromHours(24),
                            accessToken,
                            cancellationToken))
                        .ToList();

                    foreach (var metric in providerMetrics)
                    {
                        metric.ResourceId = resource.Id;
                        metric.Dimensions ??= new Dictionary<string, string>();
                        metric.Dimensions["provider"] = connection.Provider;
                        metric.Dimensions["subscriptionId"] = resource.SubscriptionId;
                        metric.Dimensions["resourceType"] = resource.Type;
                    }

                    metricsToPersist.AddRange(providerMetrics);
                }

                if (metricsToPersist.Count > 0)
                {
                    _dbContext.ObservabilityMetrics.AddRange(metricsToPersist);
                }

                var periodStart = new DateTimeOffset(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
                var actualResourceCosts = (await provider.GetResourceCostsAsync(
                        connection.SubscriptionId,
                        resources,
                        accessToken,
                        cancellationToken))
                    .ToDictionary(cost => cost.ResourceId, StringComparer.OrdinalIgnoreCase);

                var resourceCosts = new List<CloudResourceCost>(resources.Count);
                foreach (var resource in resources)
                {
                    if (actualResourceCosts.TryGetValue(resource.Id, out var actualCost))
                    {
                        actualCost.UserId = user.Id;
                        actualCost.Provider = connection.Provider;
                        actualCost.SubscriptionId = resource.SubscriptionId;
                        actualCost.ResourceGroupName = string.IsNullOrWhiteSpace(actualCost.ResourceGroupName)
                            ? resource.ResourceGroupName
                            : actualCost.ResourceGroupName;
                        actualCost.PeriodStart = periodStart;
                        actualCost.PeriodEnd = DateTimeOffset.UtcNow;
                        actualCost.CapturedAt = DateTimeOffset.UtcNow;
                        actualCost.RawData ??= JsonSerializer.Serialize(new
                        {
                            source = "provider-actual-sync",
                            resourceType = resource.Type,
                            location = resource.Location
                        });

                        resourceCosts.Add(actualCost);
                        continue;
                    }

                    var estimatedAmount = await provider.GetPriceEstimateAsync(
                        resource.Type,
                        resource.Location,
                        resource.Tags,
                        accessToken,
                        cancellationToken);

                    resourceCosts.Add(new CloudResourceCost
                    {
                        UserId = user.Id,
                        ResourceId = resource.Id,
                        Provider = connection.Provider,
                        SubscriptionId = resource.SubscriptionId,
                        ResourceGroupName = resource.ResourceGroupName,
                        Amount = decimal.Round(estimatedAmount, 2),
                        Currency = "USD",
                        Granularity = "Monthly",
                        PeriodStart = periodStart,
                        PeriodEnd = DateTimeOffset.UtcNow,
                        CapturedAt = DateTimeOffset.UtcNow,
                        CostSource = "Estimated",
                        RawData = JsonSerializer.Serialize(new
                        {
                            source = "estimated-sync-fallback",
                            resourceType = resource.Type,
                            location = resource.Location
                        })
                    });
                }

                var recommendations = (await provider.GetCostRecommendationsAsync(accessToken, cancellationToken))
                    .Select(recommendation =>
                    {
                        recommendation.Provider = connection.Provider;
                        recommendation.SubscriptionId = string.IsNullOrWhiteSpace(recommendation.SubscriptionId)
                            ? connection.SubscriptionId
                            : recommendation.SubscriptionId;
                        return recommendation;
                    })
                    .ToList();

                var existingRecommendations = await _dbContext.CostRecommendations
                    .Where(recommendation =>
                        recommendation.Provider == connection.Provider &&
                        recommendation.SubscriptionId == connection.SubscriptionId)
                    .ToListAsync(cancellationToken);

                if (existingRecommendations.Count > 0)
                {
                    _dbContext.CostRecommendations.RemoveRange(existingRecommendations);
                }

                if (recommendations.Count > 0)
                {
                    _dbContext.CostRecommendations.AddRange(recommendations);
                }

                var existingResourceCosts = await _dbContext.CloudResourceCosts
                    .Where(cost =>
                        cost.UserId == user.Id &&
                        cost.Provider == connection.Provider &&
                        cost.SubscriptionId == connection.SubscriptionId &&
                        cost.Granularity == "Monthly" &&
                        cost.PeriodStart == periodStart)
                    .ToListAsync(cancellationToken);

                if (existingResourceCosts.Count > 0)
                {
                    _dbContext.CloudResourceCosts.RemoveRange(existingResourceCosts);
                }

                if (resourceCosts.Count > 0)
                {
                    _dbContext.CloudResourceCosts.AddRange(resourceCosts);
                }

                var preferredSnapshotCosts = resourceCosts.Any(cost => !string.Equals(cost.CostSource, "Estimated", StringComparison.OrdinalIgnoreCase))
                    ? resourceCosts.Where(cost => !string.Equals(cost.CostSource, "Estimated", StringComparison.OrdinalIgnoreCase)).ToList()
                    : resourceCosts;
                var estimatedMonthlyCost = preferredSnapshotCosts.Sum(cost => cost.Amount);
                var existingSnapshots = await _dbContext.CloudCostSnapshots
                    .Where(snapshot =>
                        snapshot.UserId == user.Id &&
                        snapshot.Provider == connection.Provider &&
                        snapshot.SubscriptionId == connection.SubscriptionId &&
                        snapshot.Granularity == "Monthly" &&
                        snapshot.PeriodStart == periodStart)
                    .ToListAsync(cancellationToken);

                if (existingSnapshots.Count > 0)
                {
                    _dbContext.CloudCostSnapshots.RemoveRange(existingSnapshots);
                }

                _dbContext.CloudCostSnapshots.Add(new CloudCostSnapshot
                {
                    UserId = user.Id,
                    Provider = connection.Provider,
                    SubscriptionId = connection.SubscriptionId,
                    Amount = decimal.Round(estimatedMonthlyCost, 2),
                    Currency = resourceCosts.Select(cost => cost.Currency).FirstOrDefault(currency => !string.IsNullOrWhiteSpace(currency)) ?? "USD",
                    Granularity = "Monthly",
                    PeriodStart = periodStart,
                    PeriodEnd = DateTimeOffset.UtcNow,
                    CapturedAt = DateTimeOffset.UtcNow,
                    RawData = JsonSerializer.Serialize(new
                    {
                        source = preferredSnapshotCosts.Count == resourceCosts.Count
                            ? "resource-cost-sync"
                            : "actual-preferred-resource-cost-sync",
                        resourceCount = resources.Count,
                        actualCostCount = resourceCosts.Count(cost => !string.Equals(cost.CostSource, "Estimated", StringComparison.OrdinalIgnoreCase)),
                        estimatedFallbackCount = resourceCosts.Count(cost => string.Equals(cost.CostSource, "Estimated", StringComparison.OrdinalIgnoreCase)),
                        snapshotCostCount = preferredSnapshotCosts.Count
                    })
                });

                connection.LastSyncedAt = DateTimeOffset.UtcNow;
                connection.SyncStatus = "Healthy";
                connection.SyncMessage = $"Synced {resources.Count} resources, {providerBudgets.Count} budgets, {metricsToPersist.Count} metrics, {recommendations.Count} recommendations, and refreshed {resourceCosts.Count} cost allocations ({resourceCosts.Count(cost => !string.Equals(cost.CostSource, "Estimated", StringComparison.OrdinalIgnoreCase))} actual).";

                syncResults.Add(new CloudConnectionSyncOutcome
                {
                    ConnectionId = connection.Id,
                    Provider = connection.Provider,
                    SubscriptionId = connection.SubscriptionId,
                    Success = true,
                    Resources = resources.Count,
                    Budgets = providerBudgets.Count,
                    Metrics = metricsToPersist.Count,
                    ResourceCosts = resourceCosts.Count,
                    ActualResourceCosts = resourceCosts.Count(cost => !string.Equals(cost.CostSource, "Estimated", StringComparison.OrdinalIgnoreCase)),
                    Recommendations = recommendations.Count,
                    Message = connection.SyncMessage
                });
            }
            catch (Exception ex)
            {
                connection.LastSyncedAt = DateTimeOffset.UtcNow;
                connection.SyncStatus = "Failed";
                connection.SyncMessage = ex.Message;

                syncResults.Add(new CloudConnectionSyncOutcome
                {
                    ConnectionId = connection.Id,
                    Provider = connection.Provider,
                    SubscriptionId = connection.SubscriptionId,
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        var notificationRefresh = await _notificationEvaluationService.RefreshUserNotificationsAsync(user.Id, cancellationToken);

        return new CloudConnectionSyncResult
        {
            Connections = syncResults.Count,
            Results = syncResults,
            Notifications = notificationRefresh
        };
    }

    private async Task<string> ResolveProviderAccessTokenAsync(
        CloudConnection connection,
        CancellationToken cancellationToken)
    {
        var provider = NormalizeProvider(connection.Provider);
        return provider switch
        {
            "Azure" => await EnsureAzureAccessTokenAsync(
                connection,
                _configuration,
                _loggerFactory.CreateLogger("CloudConnectionAccess"),
                cancellationToken),
            "AWS" => BuildAwsAccessToken(connection),
            _ => connection.AccessToken ?? string.Empty
        };
    }

    private static string BuildAwsAccessToken(CloudConnection connection)
    {
        if (string.IsNullOrWhiteSpace(connection.AccessToken))
        {
            return connection.SubscriptionId;
        }

        return connection.AccessToken!;
    }

    private static string NormalizeProvider(string provider) =>
        provider.Trim().ToUpperInvariant() switch
        {
            "AZURE" => "Azure",
            "AWS" => "AWS",
            "GCP" => "GCP",
            _ => provider.Trim()
        };

    private static string ExtractResourceGroupName(string resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return "default";
        }

        var segments = resourceId.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (segments[index].Equals("resourceGroups", StringComparison.OrdinalIgnoreCase))
            {
                return segments[index + 1];
            }
        }

        return "default";
    }

    private static async Task<string> EnsureAzureAccessTokenAsync(
        CloudConnection connection,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(connection.AccessToken) &&
            connection.TokenExpiresAt.HasValue &&
            connection.TokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return connection.AccessToken;
        }

        if (string.IsNullOrWhiteSpace(connection.RefreshToken))
        {
            throw new InvalidOperationException("Azure connection is missing a refresh token. Reconnect the Azure subscription.");
        }

        using var client = new HttpClient();
        var tokenResponse = await ExchangeAzureTokenAsync(
            client,
            configuration,
            logger,
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = connection.RefreshToken!,
                ["scope"] = "https://management.azure.com/user_impersonation offline_access openid profile email"
            },
            cancellationToken);

        connection.AccessToken = tokenResponse.access_token;
        connection.RefreshToken = string.IsNullOrWhiteSpace(tokenResponse.refresh_token)
            ? connection.RefreshToken
            : tokenResponse.refresh_token;
        connection.TokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.expires_in);

        return connection.AccessToken;
    }

    private static async Task<AzureTokenResponse> ExchangeAzureTokenAsync(
        HttpClient client,
        IConfiguration configuration,
        ILogger logger,
        IReadOnlyDictionary<string, string> grantPayload,
        CancellationToken cancellationToken)
    {
        var clientId = configuration["AZURE_CLIENT_ID"];
        var clientSecret = configuration["AZURE_CLIENT_SECRET"];
        var tenantId = string.IsNullOrWhiteSpace(configuration["AZURE_TENANT_ID"])
            ? "common"
            : configuration["AZURE_TENANT_ID"]!.Trim();

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException("Azure OAuth is not configured. AZURE_CLIENT_ID and AZURE_CLIENT_SECRET are required.");
        }

        var payload = new Dictionary<string, string>(grantPayload)
        {
            ["client_id"] = clientId.Trim(),
            ["client_secret"] = clientSecret.Trim()
        };

        using var content = new FormUrlEncodedContent(payload);
        using var response = await client.PostAsync(
            $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token",
            content,
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Azure token exchange failed with status {StatusCode}: {Body}", (int)response.StatusCode, body);
            throw new HttpRequestException($"Azure token exchange failed with status {(int)response.StatusCode}.");
        }

        var tokenResponse = JsonSerializer.Deserialize<AzureTokenResponse>(body);
        if (tokenResponse is null || string.IsNullOrWhiteSpace(tokenResponse.access_token))
        {
            throw new HttpRequestException("Azure token exchange did not return an access token.");
        }

        return tokenResponse;
    }

    private sealed record AzureTokenResponse(string access_token, string? refresh_token, int expires_in);
}
