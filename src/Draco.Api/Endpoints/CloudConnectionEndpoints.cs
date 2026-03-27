using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Azure.Core;
using Azure.ResourceManager;
using Draco.Application.Interfaces;
using Draco.Domain.Entities;
using Draco.Domain.Repositories;
using Draco.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Draco.Api.Endpoints;

public static class CloudConnectionEndpoints
{
    private const string AzureManagementScope = "https://management.azure.com/user_impersonation offline_access openid profile email";

    public static void MapCloudConnectionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/cloud-connections").RequireAuthorization();

        group.MapGet("/", ListConnectionsAsync)
            .WithName("ListCloudConnections");

        group.MapGet("/azure/authorize-url", GetAzureAuthorizeUrlAsync)
            .WithName("GetAzureAuthorizeUrl");

        group.MapPost("/azure/exchange", ExchangeAzureCodeAsync)
            .WithName("ExchangeAzureCode");

        group.MapPost("/", UpsertConnectionAsync)
            .WithName("UpsertCloudConnection");

        group.MapDelete("/{id:int}", DeleteConnectionAsync)
            .WithName("DeleteCloudConnection");

        group.MapPost("/sync", SyncConnectionsAsync)
            .WithName("SyncCloudConnections");
    }

    private static async Task<IResult> ListConnectionsAsync(
        ClaimsPrincipal userPrincipal,
        DracoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var user = await userPrincipal.GetCurrentUserAsync(dbContext, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(user.Connections
            .OrderByDescending(connection => connection.LastSyncedAt ?? connection.ConnectedAt)
            .Select(ToConnectionDto));
    }

    private static IResult GetAzureAuthorizeUrlAsync(
        [FromQuery] string? redirectUri,
        [FromQuery] string? state,
        IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(redirectUri) || !Uri.TryCreate(redirectUri, UriKind.Absolute, out _))
        {
            return Results.BadRequest(new { message = "A valid redirect URI is required." });
        }

        if (string.IsNullOrWhiteSpace(state))
        {
            return Results.BadRequest(new { message = "OAuth state is required." });
        }

        var clientId = configuration["AZURE_CLIENT_ID"];
        var tenantId = GetAzureTenantSegment(configuration);

        if (string.IsNullOrWhiteSpace(clientId))
        {
            return Results.Problem("Azure OAuth is not configured on the API.", statusCode: StatusCodes.Status500InternalServerError);
        }

        var authorizeUrl = new UriBuilder($"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize");
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = clientId,
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri,
            ["response_mode"] = "query",
            ["scope"] = AzureManagementScope,
            ["state"] = state,
            ["prompt"] = "select_account"
        };

        authorizeUrl.Query = string.Join("&", query.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value ?? string.Empty)}"));

        return Results.Ok(new { authorizeUrl = authorizeUrl.ToString() });
    }

    private static async Task<IResult> ExchangeAzureCodeAsync(
        [FromBody] AzureCodeExchangeRequest request,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.RedirectUri))
        {
            return Results.BadRequest(new { message = "Authorization code and redirect URI are required." });
        }

        if (!Uri.TryCreate(request.RedirectUri, UriKind.Absolute, out _))
        {
            return Results.BadRequest(new { message = "Redirect URI must be absolute." });
        }

        var logger = loggerFactory.CreateLogger("AzureOAuth");

        try
        {
            using var client = httpClientFactory.CreateClient();
            var tokenResponse = await ExchangeAzureTokenAsync(
                client,
                configuration,
                logger,
                new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = request.Code.Trim(),
                    ["redirect_uri"] = request.RedirectUri.Trim(),
                    ["scope"] = AzureManagementScope
                },
                cancellationToken);

            var subscriptions = await ListAzureSubscriptionsAsync(tokenResponse.access_token, cancellationToken);

            return Results.Ok(new AzureExchangeResponse(
                tokenResponse.access_token,
                tokenResponse.refresh_token,
                DateTimeOffset.UtcNow.AddSeconds(tokenResponse.expires_in),
                subscriptions));
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Azure OAuth exchange configuration is invalid.");
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Azure OAuth exchange failed.");
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static async Task<IResult> UpsertConnectionAsync(
        [FromBody] CloudConnectionRequest request,
        ClaimsPrincipal userPrincipal,
        DracoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var user = await userPrincipal.GetCurrentUserAsync(dbContext, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Provider) || string.IsNullOrWhiteSpace(request.SubscriptionId))
        {
            return Results.BadRequest(new { message = "Provider and subscription ID are required." });
        }

        var provider = AuthEndpoints.NormalizeProvider(request.Provider);
        var subscriptionId = request.SubscriptionId.Trim();

        var connection = user.Connections.FirstOrDefault(existing =>
            existing.Provider == provider &&
            existing.SubscriptionId == subscriptionId);

        if (connection is null)
        {
            connection = new CloudConnection
            {
                UserId = user.Id,
                Provider = provider,
                SubscriptionId = subscriptionId,
                DisplayName = request.DisplayName,
                ExternalAccountId = request.ExternalAccountId,
                AccessToken = request.AccessToken,
                RefreshToken = request.RefreshToken,
                TokenExpiresAt = request.TokenExpiresAt,
                ConnectedAt = DateTimeOffset.UtcNow,
                IsActive = true,
                SyncStatus = "Pending"
            };

            dbContext.CloudConnections.Add(connection);
        }
        else
        {
            connection.DisplayName = request.DisplayName ?? connection.DisplayName;
            connection.ExternalAccountId = request.ExternalAccountId ?? connection.ExternalAccountId;
            connection.AccessToken = request.AccessToken ?? connection.AccessToken;
            connection.RefreshToken = request.RefreshToken ?? connection.RefreshToken;
            connection.TokenExpiresAt = request.TokenExpiresAt ?? connection.TokenExpiresAt;
            connection.IsActive = request.IsActive ?? connection.IsActive;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToConnectionDto(connection));
    }

    private static async Task<IResult> DeleteConnectionAsync(
        int id,
        ClaimsPrincipal userPrincipal,
        DracoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var user = await userPrincipal.GetCurrentUserAsync(dbContext, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var connection = user.Connections.FirstOrDefault(existing => existing.Id == id);
        if (connection is null)
        {
            return Results.NotFound(new { message = "Cloud connection not found." });
        }

        connection.IsActive = false;
        connection.SyncStatus = "Disconnected";
        connection.SyncMessage = "Connection disabled by user.";

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { message = "Cloud connection disabled." });
    }

    private static async Task<IResult> SyncConnectionsAsync(
        [FromBody] SyncConnectionsRequest? request,
        ClaimsPrincipal userPrincipal,
        DracoDbContext dbContext,
        IEnumerable<ICloudProvider> providers,
        IEnumerable<INotificationRule> notificationRules,
        INotificationEvaluationService notificationEvaluationService,
        IResourceRepository resourceRepository,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var user = await userPrincipal.GetCurrentUserAsync(dbContext, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var providerMap = providers.ToDictionary(
            provider => AuthEndpoints.NormalizeProvider(provider.ProviderName),
            StringComparer.OrdinalIgnoreCase);

        var requestedIds = request?.ConnectionIds?.ToHashSet();
        var targetConnections = user.Connections
            .Where(connection => connection.IsActive)
            .Where(connection => requestedIds is null || requestedIds.Contains(connection.Id))
            .ToList();

        var syncResults = new List<object>();

        foreach (var connection in targetConnections)
        {
            if (!providerMap.TryGetValue(connection.Provider, out var provider))
            {
                connection.SyncStatus = "Unsupported";
                connection.SyncMessage = $"Provider '{connection.Provider}' is not configured on the API.";
                syncResults.Add(new
                {
                    connectionId = connection.Id,
                    provider = connection.Provider,
                    subscriptionId = connection.SubscriptionId,
                    success = false,
                    message = connection.SyncMessage
                });
                continue;
            }

            try
            {
                var accessToken = connection.AccessToken;

                if (string.Equals(connection.Provider, "Azure", StringComparison.OrdinalIgnoreCase))
                {
                    accessToken = await EnsureAzureAccessTokenAsync(
                        connection,
                        configuration,
                        httpClientFactory,
                        loggerFactory.CreateLogger("AzureOAuthRefresh"),
                        cancellationToken);
                }

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
                    await resourceRepository.UpsertResourcesAsync(resources, cancellationToken);
                }

                var providerBudgets = (await provider.GetBudgetsAsync(
                        connection.SubscriptionId,
                        accessToken,
                        cancellationToken))
                    .ToList();

                var existingImportedBudgets = await dbContext.CostBudgets
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
                    dbContext.CostBudgets.Remove(staleBudget);
                }

                foreach (var importedBudget in providerBudgets)
                {
                    var existingBudget = existingImportedBudgets.FirstOrDefault(budget =>
                        string.Equals(budget.ExternalBudgetId, importedBudget.ExternalBudgetId, StringComparison.OrdinalIgnoreCase));

                    if (existingBudget is null)
                    {
                        dbContext.CostBudgets.Add(new CostBudget
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
                    var requestedMetricNames = notificationRules
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
                    dbContext.ObservabilityMetrics.AddRange(metricsToPersist);
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

                var existingRecommendations = await dbContext.CostRecommendations
                    .Where(recommendation =>
                        recommendation.Provider == connection.Provider &&
                        recommendation.SubscriptionId == connection.SubscriptionId)
                    .ToListAsync(cancellationToken);

                if (existingRecommendations.Count > 0)
                {
                    dbContext.CostRecommendations.RemoveRange(existingRecommendations);
                }

                if (recommendations.Count > 0)
                {
                    dbContext.CostRecommendations.AddRange(recommendations);
                }

                var existingResourceCosts = await dbContext.CloudResourceCosts
                    .Where(cost =>
                        cost.UserId == user.Id &&
                        cost.Provider == connection.Provider &&
                        cost.SubscriptionId == connection.SubscriptionId &&
                        cost.Granularity == "Monthly" &&
                        cost.PeriodStart == periodStart)
                    .ToListAsync(cancellationToken);

                if (existingResourceCosts.Count > 0)
                {
                    dbContext.CloudResourceCosts.RemoveRange(existingResourceCosts);
                }

                if (resourceCosts.Count > 0)
                {
                    dbContext.CloudResourceCosts.AddRange(resourceCosts);
                }

                var estimatedMonthlyCost = resourceCosts.Sum(cost => cost.Amount);
                var existingSnapshots = await dbContext.CloudCostSnapshots
                    .Where(snapshot =>
                        snapshot.UserId == user.Id &&
                        snapshot.Provider == connection.Provider &&
                        snapshot.SubscriptionId == connection.SubscriptionId &&
                        snapshot.Granularity == "Monthly" &&
                        snapshot.PeriodStart == periodStart)
                    .ToListAsync(cancellationToken);

                if (existingSnapshots.Count > 0)
                {
                    dbContext.CloudCostSnapshots.RemoveRange(existingSnapshots);
                }

                dbContext.CloudCostSnapshots.Add(new CloudCostSnapshot
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
                        source = "resource-cost-sync",
                        resourceCount = resources.Count,
                        actualCostCount = resourceCosts.Count(cost => !string.Equals(cost.CostSource, "Estimated", StringComparison.OrdinalIgnoreCase)),
                        estimatedFallbackCount = resourceCosts.Count(cost => string.Equals(cost.CostSource, "Estimated", StringComparison.OrdinalIgnoreCase))
                    })
                });

                connection.LastSyncedAt = DateTimeOffset.UtcNow;
                connection.SyncStatus = "Healthy";
                connection.SyncMessage = $"Synced {resources.Count} resources, {providerBudgets.Count} budgets, {metricsToPersist.Count} metrics, {recommendations.Count} recommendations, and refreshed {resourceCosts.Count} cost allocations ({resourceCosts.Count(cost => !string.Equals(cost.CostSource, "Estimated", StringComparison.OrdinalIgnoreCase))} actual).";

                syncResults.Add(new
                {
                    connectionId = connection.Id,
                    provider = connection.Provider,
                    subscriptionId = connection.SubscriptionId,
                    success = true,
                    resources = resources.Count,
                    budgets = providerBudgets.Count,
                    metrics = metricsToPersist.Count,
                    resourceCosts = resourceCosts.Count,
                    actualResourceCosts = resourceCosts.Count(cost => !string.Equals(cost.CostSource, "Estimated", StringComparison.OrdinalIgnoreCase)),
                    recommendations = recommendations.Count,
                    message = connection.SyncMessage
                });
            }
            catch (Exception ex)
            {
                connection.LastSyncedAt = DateTimeOffset.UtcNow;
                connection.SyncStatus = "Failed";
                connection.SyncMessage = ex.Message;

                syncResults.Add(new
                {
                    connectionId = connection.Id,
                    provider = connection.Provider,
                    subscriptionId = connection.SubscriptionId,
                    success = false,
                    message = ex.Message
                });
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        var notificationRefresh = await notificationEvaluationService.RefreshUserNotificationsAsync(user.Id, cancellationToken);

        return Results.Ok(new
        {
            connections = syncResults.Count,
            results = syncResults,
            notifications = notificationRefresh
        });
    }

    private static object ToConnectionDto(CloudConnection connection) => new
    {
        id = connection.Id,
        provider = connection.Provider,
        subscriptionId = connection.SubscriptionId,
        displayName = connection.DisplayName,
        externalAccountId = connection.ExternalAccountId,
        isActive = connection.IsActive,
        connectedAt = connection.ConnectedAt,
        lastSyncedAt = connection.LastSyncedAt,
        syncStatus = connection.SyncStatus,
        syncMessage = connection.SyncMessage
    };

    private static string GetAzureTenantSegment(IConfiguration configuration) =>
        string.IsNullOrWhiteSpace(configuration["AZURE_TENANT_ID"])
            ? "organizations"
            : configuration["AZURE_TENANT_ID"]!.Trim();

    private static async Task<string?> EnsureAzureAccessTokenAsync(
        CloudConnection connection,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var expiresSoon = connection.TokenExpiresAt.HasValue &&
            connection.TokenExpiresAt.Value <= DateTimeOffset.UtcNow.AddMinutes(5);

        if (!string.IsNullOrWhiteSpace(connection.AccessToken) && !expiresSoon)
        {
            return connection.AccessToken;
        }

        if (string.IsNullOrWhiteSpace(connection.RefreshToken))
        {
            if (!string.IsNullOrWhiteSpace(connection.AccessToken))
            {
                return connection.AccessToken;
            }

            throw new InvalidOperationException("Azure connection requires re-authentication.");
        }

        using var client = httpClientFactory.CreateClient();
        var tokenResponse = await ExchangeAzureTokenAsync(
            client,
            configuration,
            logger,
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = connection.RefreshToken,
                ["scope"] = AzureManagementScope
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
        IReadOnlyDictionary<string, string> grantValues,
        CancellationToken cancellationToken)
    {
        var clientId = configuration["AZURE_CLIENT_ID"];
        var clientSecret = configuration["AZURE_CLIENT_SECRET"];
        var tenantId = GetAzureTenantSegment(configuration);

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException("Azure OAuth is not configured. AZURE_CLIENT_ID and AZURE_CLIENT_SECRET are required.");
        }

        var payload = new Dictionary<string, string>(grantValues)
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token")
        {
            Content = new FormUrlEncodedContent(payload)
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("Azure OAuth exchange failed with status {StatusCode}: {ErrorContent}", (int)response.StatusCode, errorContent);
            throw new HttpRequestException("Microsoft sign-in could not be completed.");
        }

        var tokenResponse = await response.Content.ReadFromJsonAsync<AzureTokenResponse>(cancellationToken: cancellationToken);
        if (tokenResponse is null || string.IsNullOrWhiteSpace(tokenResponse.access_token))
        {
            throw new HttpRequestException("Microsoft sign-in returned an invalid token response.");
        }

        return tokenResponse;
    }

    private static async Task<List<AzureSubscriptionOption>> ListAzureSubscriptionsAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        var client = new ArmClient(new AzureBearerTokenCredential(accessToken));
        var subscriptions = new List<AzureSubscriptionOption>();

        await foreach (var subscription in client.GetSubscriptions().GetAllAsync(cancellationToken))
        {
            subscriptions.Add(new AzureSubscriptionOption(
                subscription.Data.SubscriptionId ?? string.Empty,
                string.IsNullOrWhiteSpace(subscription.Data.DisplayName)
                    ? subscription.Data.SubscriptionId ?? "Unnamed subscription"
                    : subscription.Data.DisplayName,
                subscription.Data.State.ToString()));
        }

        return subscriptions
            .Where(subscription => !string.IsNullOrWhiteSpace(subscription.SubscriptionId))
            .OrderBy(subscription => subscription.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

public sealed record CloudConnectionRequest(
    string Provider,
    string SubscriptionId,
    string? DisplayName,
    string? ExternalAccountId,
    string? AccessToken,
    string? RefreshToken,
    DateTimeOffset? TokenExpiresAt,
    bool? IsActive);

public sealed record SyncConnectionsRequest(List<int>? ConnectionIds);
public sealed record AzureCodeExchangeRequest(string Code, string RedirectUri);
public sealed record AzureExchangeResponse(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset TokenExpiresAt,
    List<AzureSubscriptionOption> Subscriptions);
public sealed record AzureSubscriptionOption(string SubscriptionId, string DisplayName, string? State);

public sealed class AzureTokenResponse
{
    public string access_token { get; set; } = string.Empty;
    public string? refresh_token { get; set; }
    public int expires_in { get; set; }
}

internal sealed class AzureBearerTokenCredential(string accessToken) : TokenCredential
{
    private readonly AccessToken _token = new(accessToken, DateTimeOffset.UtcNow.AddHours(1));

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => _token;

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_token);
}
