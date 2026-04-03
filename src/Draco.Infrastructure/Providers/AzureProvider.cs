using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Draco.Application.Interfaces;
using Draco.Application.Models;
using Draco.Domain.Entities;
using System.Net.Http.Headers;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Draco.Infrastructure.Providers;

public class AzureProvider : ICloudProvider
{
    private const string UsageDetailsApiVersion = "2024-08-01";
    private const string CostManagementQueryApiVersion = "2025-03-01";
    private const string MetricsApiVersion = "2023-10-01";
    private const string BillingAccountsApiVersion = "2024-04-01";
    private readonly ILogger<AzureProvider> _logger;
    private ArmClient? _armClient;

    public string ProviderName => "Azure";

    public AzureProvider(ILogger<AzureProvider> logger)
    {
        _logger = logger;
    }

    private ArmClient GetClient(string? accessToken)
    {
        if (_armClient != null && accessToken == null) return _armClient;
        
        if (!string.IsNullOrEmpty(accessToken))
        {
            _logger.LogInformation("Using provided OAuth token for Azure connection.");
            return new ArmClient(new SimpleTokenCredential(accessToken));
        }

        return _armClient ??= new ArmClient(new DefaultAzureCredential());
    }

    private class SimpleTokenCredential : TokenCredential
    {
        private readonly AccessToken _token;
        public SimpleTokenCredential(string token)
        {
            _token = new AccessToken(token, DateTimeOffset.UtcNow.AddHours(1));
        }
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) => new(_token);
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => _token;
    }

    public async Task<IEnumerable<CloudResource>> ListResourcesAsync(string? accessToken = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting Azure resource discovery...");
        var resources = new List<CloudResource>();
        var client = GetClient(accessToken);

        try
        {
            await foreach (var subscription in client.GetSubscriptions().GetAllAsync(cancellationToken))
            {
                _logger.LogDebug("Scanning subscription: {SubscriptionId}", subscription.Data.SubscriptionId);
                
                await foreach (var resource in subscription.GetGenericResourcesAsync(cancellationToken: cancellationToken))
                {
                    var resourceId = resource.Data.Id.ToString();

                    resources.Add(new CloudResource
                    {
                        Id = resourceId,
                        Name = resource.Data.Name,
                        Type = resource.Data.ResourceType.ToString(),
                        Provider = ProviderName,
                        Location = resource.Data.Location.Name ?? string.Empty,
                        SubscriptionId = subscription.Data.SubscriptionId!,
                        ResourceGroupName = ExtractResourceGroupName(resourceId),
                        Tags = resource.Data.Tags.ToDictionary(k => k.Key, v => v.Value),
                        DiscoveredAt = DateTimeOffset.UtcNow
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list Azure resources.");
            throw;
        }

        _logger.LogInformation("Discovered {Count} resources in Azure.", resources.Count);
        return resources;
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

    public async Task<IEnumerable<ProviderBudgetSnapshot>> GetBudgetsAsync(
        string subscriptionId,
        string? accessToken = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(subscriptionId))
        {
            return [];
        }

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var scopes = new Dictionary<string, (string ScopeType, string? ScopeDisplayName)>(StringComparer.OrdinalIgnoreCase)
        {
            [$"/subscriptions/{subscriptionId}"] = ("Subscription", subscriptionId)
        };

        foreach (var billingScope in await GetAccessibleBillingAccountScopesAsync(client, cancellationToken))
        {
            scopes[billingScope.Scope] = (billingScope.ScopeType, billingScope.ScopeDisplayName);
        }

        var budgets = new Dictionary<string, ProviderBudgetSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var scope in scopes)
        {
            foreach (var budget in await GetBudgetsForScopeAsync(
                         client,
                         scope.Key,
                         scope.Value.ScopeType,
                         scope.Value.ScopeDisplayName,
                         cancellationToken))
            {
                budgets[budget.ExternalBudgetId] = budget;
            }
        }

        return budgets.Values.ToList();
    }

    public async Task<IEnumerable<CloudResourceCost>> GetResourceCostsAsync(
        string subscriptionId,
        IEnumerable<CloudResource> resources,
        string? accessToken = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            _logger.LogWarning("Azure actual cost ingestion skipped because no access token was available.");
            return [];
        }

        var resourceMap = resources
            .Where(resource => !string.IsNullOrWhiteSpace(resource.Id))
            .ToDictionary(resource => NormalizeResourceId(resource.Id), StringComparer.OrdinalIgnoreCase);

        if (resourceMap.Count == 0)
        {
            return [];
        }

        var periodStart = new DateTimeOffset(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var queryItems = await GetResourceCostsFromCostQueryAsync(subscriptionId, resourceMap, client, cancellationToken);
        if (queryItems.Count > 0)
        {
            return queryItems.Select(item => new CloudResourceCost
            {
                ResourceId = item.Key,
                Provider = ProviderName,
                SubscriptionId = subscriptionId,
                ResourceGroupName = item.Value.ResourceGroupName,
                Amount = decimal.Round(item.Value.Amount, 2),
                Currency = item.Value.Currency,
                Granularity = "Monthly",
                PeriodStart = periodStart,
                PeriodEnd = DateTimeOffset.UtcNow,
                CapturedAt = item.Value.CapturedAt,
                CostSource = "AzureActual"
            }).ToList();
        }

        var usageItems = await GetResourceCostsFromUsageDetailsAsync(subscriptionId, periodStart, resourceMap, client, cancellationToken);
        return usageItems.Select(item => new CloudResourceCost
        {
            ResourceId = item.Key,
            Provider = ProviderName,
            SubscriptionId = subscriptionId,
            ResourceGroupName = item.Value.ResourceGroupName,
            Amount = decimal.Round(item.Value.Amount, 2),
            Currency = item.Value.Currency,
            Granularity = "Monthly",
            PeriodStart = periodStart,
            PeriodEnd = DateTimeOffset.UtcNow,
            CapturedAt = item.Value.CapturedAt,
            CostSource = "AzureActual"
        }).ToList();
    }

    private async Task<Dictionary<string, (decimal Amount, string Currency, string ResourceGroupName, DateTimeOffset CapturedAt)>> GetResourceCostsFromCostQueryAsync(
        string subscriptionId,
        IReadOnlyDictionary<string, CloudResource> resourceMap,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var usageItems = new Dictionary<string, (decimal Amount, string Currency, string ResourceGroupName, DateTimeOffset CapturedAt)>(StringComparer.OrdinalIgnoreCase);
        var requestUri = $"{BuildAzureManagementUri($"/subscriptions/{subscriptionId}")}/providers/Microsoft.CostManagement/query?api-version={CostManagementQueryApiVersion}";
        var requestBody = JsonSerializer.Serialize(new
        {
            type = "Usage",
            timeframe = "MonthToDate",
            dataset = new
            {
                granularity = "None",
                aggregation = new
                {
                    totalCost = new
                    {
                        name = "PreTaxCost",
                        function = "Sum"
                    }
                },
                grouping = new object[]
                {
                    new { type = "Dimension", name = "ResourceId" },
                    new { type = "Dimension", name = "ResourceGroup" }
                }
            }
        });

        while (!string.IsNullOrWhiteSpace(requestUri))
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
            };

            using var response = await client.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                break;
            }

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogInformation("Azure Cost Management query failed for subscription {SubscriptionId}: {StatusCode} {Body}", subscriptionId, response.StatusCode, error);
                return [];
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("properties", out var properties))
            {
                break;
            }

            var columnIndexes = BuildColumnIndexMap(properties);
            if (!columnIndexes.TryGetValue("PreTaxCost", out var amountIndex) &&
                !columnIndexes.TryGetValue("Cost", out amountIndex))
            {
                _logger.LogInformation("Azure Cost Management query response for subscription {SubscriptionId} did not include a usable cost column.", subscriptionId);
                return [];
            }

            var resourceIdIndex = GetColumnIndex(columnIndexes, "ResourceId");
            if (resourceIdIndex < 0)
            {
                _logger.LogInformation("Azure Cost Management query response for subscription {SubscriptionId} did not include ResourceId grouping.", subscriptionId);
                return [];
            }

            var resourceGroupIndex = GetColumnIndex(columnIndexes, "ResourceGroup", "ResourceGroupName");
            var currencyIndex = GetColumnIndex(columnIndexes, "Currency");

            if (properties.TryGetProperty("rows", out var rows))
            {
                foreach (var row in rows.EnumerateArray())
                {
                    var resourceId = NormalizeResourceId(GetArrayString(row, resourceIdIndex));
                    if (string.IsNullOrWhiteSpace(resourceId) || !resourceMap.ContainsKey(resourceId))
                    {
                        continue;
                    }

                    var amount = GetArrayDecimal(row, amountIndex);
                    if (!amount.HasValue)
                    {
                        continue;
                    }

                    var resourceGroupName = GetArrayString(row, resourceGroupIndex)
                        ?? resourceMap[resourceId].ResourceGroupName;
                    var currency = GetArrayString(row, currencyIndex) ?? "USD";

                    if (usageItems.TryGetValue(resourceId, out var existing))
                    {
                        usageItems[resourceId] = (existing.Amount + amount.Value, currency, resourceGroupName, DateTimeOffset.UtcNow);
                    }
                    else
                    {
                        usageItems[resourceId] = (amount.Value, currency, resourceGroupName, DateTimeOffset.UtcNow);
                    }
                }
            }

            requestUri = properties.TryGetProperty("nextLink", out var nextLinkElement)
                ? nextLinkElement.GetString()
                : null;
        }

        return usageItems;
    }

    private async Task<Dictionary<string, (decimal Amount, string Currency, string ResourceGroupName, DateTimeOffset CapturedAt)>> GetResourceCostsFromUsageDetailsAsync(
        string subscriptionId,
        DateTimeOffset periodStart,
        IReadOnlyDictionary<string, CloudResource> resourceMap,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var periodEndExclusive = periodStart.AddMonths(1);
        var usageUri = $"https://management.azure.com/subscriptions/{subscriptionId}/providers/Microsoft.Consumption/usageDetails?api-version={UsageDetailsApiVersion}&metric=ActualCost&$top=1000";
        var usageItems = new Dictionary<string, (decimal Amount, string Currency, string ResourceGroupName, DateTimeOffset CapturedAt)>(StringComparer.OrdinalIgnoreCase);

        while (!string.IsNullOrWhiteSpace(usageUri))
        {
            using var response = await client.GetAsync(usageUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Azure usage details request failed for subscription {SubscriptionId}: {StatusCode} {Body}", subscriptionId, response.StatusCode, error);
                break;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;

            if (root.TryGetProperty("value", out var values))
            {
                foreach (var item in values.EnumerateArray())
                {
                    if (!item.TryGetProperty("properties", out var properties))
                    {
                        continue;
                    }

                    var resourceId = NormalizeResourceId(
                        GetString(properties, "resourceId")
                        ?? GetString(properties, "instanceName")
                        ?? GetString(properties, "instanceId"));

                    if (string.IsNullOrWhiteSpace(resourceId) || !resourceMap.ContainsKey(resourceId))
                    {
                        continue;
                    }

                    var usageDate = GetDateTimeOffset(properties, "date");
                    if (!usageDate.HasValue || usageDate.Value < periodStart || usageDate.Value >= periodEndExclusive)
                    {
                        continue;
                    }

                    var amount = GetDecimal(properties, "costInBillingCurrency")
                        ?? GetDecimal(properties, "paygCostInBillingCurrency")
                        ?? GetDecimal(properties, "cost")
                        ?? GetDecimal(properties, "costInUSD");

                    if (!amount.HasValue)
                    {
                        continue;
                    }

                    var currency = GetString(properties, "billingCurrencyCode")
                        ?? GetString(properties, "billingCurrency")
                        ?? "USD";
                    var resourceGroupName = GetString(properties, "resourceGroup")
                        ?? resourceMap[resourceId].ResourceGroupName;

                    if (usageItems.TryGetValue(resourceId, out var existing))
                    {
                        usageItems[resourceId] = (existing.Amount + amount.Value, existing.Currency, resourceGroupName, DateTimeOffset.UtcNow);
                    }
                    else
                    {
                        usageItems[resourceId] = (amount.Value, currency, resourceGroupName, DateTimeOffset.UtcNow);
                    }
                }
            }

            usageUri = root.TryGetProperty("nextLink", out var nextLinkElement)
                ? nextLinkElement.GetString()
                : null;
        }

        return usageItems;
    }

    public async Task<IEnumerable<ObservabilityMetric>> GetMetricsAsync(
        CloudResource resource,
        IEnumerable<string> metricNames,
        TimeSpan timespan,
        string? accessToken = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return [];
        }

        var mappings = ResolveMetricMappings(resource);
        var requestedMappings = metricNames
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(metricName => mappings.TryGetValue(metricName, out var mapping) ? mapping : null)
            .Where(mapping => mapping is not null)
            .Cast<AzureMetricMapping>()
            .ToList();

        if (requestedMappings.Count == 0)
        {
            return [];
        }

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var start = DateTimeOffset.UtcNow.Subtract(timespan);
        var end = DateTimeOffset.UtcNow;
        var results = new List<ObservabilityMetric>();

        foreach (var mapping in requestedMappings)
        {
            var metricResourceUri = $"{mapping.ResourceUri}/providers/Microsoft.Insights/metrics";
            var query = new Dictionary<string, string?>
            {
                ["timespan"] = $"{start:O}/{end:O}",
                ["interval"] = "PT1H",
                ["metricnames"] = mapping.ProviderMetricName,
                ["aggregation"] = mapping.Aggregation,
                ["metricnamespace"] = mapping.MetricNamespace,
                ["AutoAdjustTimegrain"] = "true",
                ["ValidateDimensions"] = "false",
                ["api-version"] = MetricsApiVersion
            };

            var requestUri = $"{BuildAzureManagementUri(metricResourceUri)}?{BuildQueryString(query)}";
            using var response = await client.GetAsync(requestUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode != HttpStatusCode.BadRequest)
                {
                    var error = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogDebug("Azure metrics request failed for {ResourceId}/{MetricName}: {StatusCode} {Body}", resource.Id, mapping.ProviderMetricName, response.StatusCode, error);
                }

                continue;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("value", out var metricValues) || metricValues.GetArrayLength() == 0)
            {
                continue;
            }

            var providerMetric = metricValues[0];
            if (!providerMetric.TryGetProperty("timeseries", out var timeSeries) || timeSeries.GetArrayLength() == 0)
            {
                continue;
            }

            var latestPoint = timeSeries
                .EnumerateArray()
                .SelectMany(series => series.TryGetProperty("data", out var data) ? data.EnumerateArray() : [])
                .Select(point => new
                {
                    Timestamp = GetDateTimeOffset(point, "timeStamp") ?? DateTimeOffset.UtcNow,
                    Value = GetDecimal(point, mapping.AggregationProperty)
                })
                .Where(item => item.Value.HasValue)
                .OrderBy(item => item.Timestamp)
                .LastOrDefault();

            if (latestPoint?.Value is null)
            {
                continue;
            }

            results.Add(new ObservabilityMetric
            {
                ResourceId = resource.Id,
                MetricName = mapping.CanonicalMetricName,
                Value = decimal.ToDouble(latestPoint.Value.Value),
                Unit = GetString(providerMetric, "unit") ?? mapping.Unit,
                Timestamp = latestPoint.Timestamp,
                Dimensions = new Dictionary<string, string>
                {
                    ["provider"] = ProviderName,
                    ["resourceType"] = resource.Type
                }
            });
        }

        return results;
    }

    public Task<IEnumerable<CostRecommendation>> GetCostRecommendationsAsync(string? accessToken = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Scanning for Azure cost recommendations...");
        _ = GetClient(accessToken);
        return Task.FromResult<IEnumerable<CostRecommendation>>([]);
    }

    public Task<decimal> GetPriceEstimateAsync(string resourceType, string location, IDictionary<string, string> parameters, string? accessToken = null, CancellationToken cancellationToken = default)
    {
        var normalizedType = resourceType.Trim().ToLowerInvariant();

        var estimate = normalizedType switch
        {
            var value when value.Contains("microsoft.compute/virtualmachines", StringComparison.Ordinal) => 120m,
            var value when value.Contains("microsoft.sql/servers/databases", StringComparison.Ordinal) => 95m,
            var value when value.Contains("microsoft.containerservice/managedclusters", StringComparison.Ordinal) => 220m,
            var value when value.Contains("microsoft.storage/storageaccounts", StringComparison.Ordinal) => 2m,
            var value when value.Contains("microsoft.web/sites", StringComparison.Ordinal) => 15m,
            var value when value.Contains("microsoft.network/loadbalancers", StringComparison.Ordinal) => 18m,
            var value when value.Contains("microsoft.documentdb/databaseaccounts", StringComparison.Ordinal) => 25m,
            var value when value.Contains("microsoft.dbformysql/flexibleservers", StringComparison.Ordinal) => 35m,
            var value when value.Contains("microsoft.machinelearningservices/workspaces", StringComparison.Ordinal) => 0m,
            var value when value.Contains("microsoft.operationalinsights/workspaces", StringComparison.Ordinal) => 0m,
            var value when value.Contains("microsoft.keyvault/vaults", StringComparison.Ordinal) => 0m,
            var value when value.Contains("microsoft.insights/components", StringComparison.Ordinal) => 0m,
            _ => 0m,
        };

        return Task.FromResult(estimate);
    }

    public Task<bool> StopResourceAsync(string resourceId, string? accessToken = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping Azure resource {ResourceId}", resourceId);
        try
        {
            _ = GetClient(accessToken);
            var resourceIdentifier = new ResourceIdentifier(resourceId);
            if (resourceIdentifier.ResourceType == "Microsoft.Compute/virtualMachines")
            {
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop Azure resource {ResourceId}", resourceId);
            return Task.FromResult(false);
        }
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static Dictionary<string, int> BuildColumnIndexMap(JsonElement properties)
    {
        if (!properties.TryGetProperty("columns", out var columns) || columns.ValueKind != JsonValueKind.Array)
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var column in columns.EnumerateArray())
        {
            var name = GetString(column, "name");
            if (!string.IsNullOrWhiteSpace(name) && !result.ContainsKey(name))
            {
                result[name] = index;
            }

            index++;
        }

        return result;
    }

    private static int GetColumnIndex(IReadOnlyDictionary<string, int> columns, params string[] names)
    {
        foreach (var name in names)
        {
            if (columns.TryGetValue(name, out var index))
            {
                return index;
            }
        }

        return -1;
    }

    private static string? GetArrayString(JsonElement row, int index)
    {
        if (index < 0 || row.ValueKind != JsonValueKind.Array || row.GetArrayLength() <= index)
        {
            return null;
        }

        var value = row[index];
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static decimal? GetArrayDecimal(JsonElement row, int index)
    {
        if (index < 0 || row.ValueKind != JsonValueKind.Array || row.GetArrayLength() <= index)
        {
            return null;
        }

        var value = row[index];
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var numeric))
        {
            return numeric;
        }

        return value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), out var parsed)
            ? parsed
            : null;
    }

    private static string? GetString(JsonElement element, string firstProperty, string nestedProperty) =>
        element.TryGetProperty(firstProperty, out var property) && property.ValueKind == JsonValueKind.Object
            ? GetString(property, nestedProperty)
            : null;

    private static DateTimeOffset? GetDateTimeOffset(JsonElement element, string propertyName)
    {
        var value = GetString(element, propertyName);
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static decimal? GetDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var numeric))
        {
            return numeric;
        }

        if (property.ValueKind == JsonValueKind.String && decimal.TryParse(property.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static decimal? TryGetNestedDecimal(JsonElement element, string firstProperty, string nestedProperty) =>
        element.TryGetProperty(firstProperty, out var property) && property.ValueKind == JsonValueKind.Object
            ? GetDecimal(property, nestedProperty)
            : null;

    private static string NormalizeResourceId(string? resourceId) =>
        string.IsNullOrWhiteSpace(resourceId)
            ? string.Empty
            : resourceId.Trim().TrimEnd('/');

    private async Task<IEnumerable<(string Scope, string ScopeType, string? ScopeDisplayName)>> GetAccessibleBillingAccountScopesAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var requestUri = $"{BuildAzureManagementUri("/providers/Microsoft.Billing/billingAccounts")}?api-version={BillingAccountsApiVersion}";
        using var response = await client.GetAsync(requestUri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogInformation("Azure billing account discovery skipped: {StatusCode} {Body}", response.StatusCode, error);
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("value", out var values))
        {
            return [];
        }

        var scopes = new List<(string Scope, string ScopeType, string? ScopeDisplayName)>();
        foreach (var account in values.EnumerateArray())
        {
            var scope = GetString(account, "id");
            if (string.IsNullOrWhiteSpace(scope))
            {
                continue;
            }

            var displayName = GetString(account, "properties", "displayName")
                ?? GetString(account, "displayName")
                ?? GetString(account, "name");

            scopes.Add((scope, "BillingAccount", displayName));
        }

        return scopes;
    }

    private async Task<IEnumerable<ProviderBudgetSnapshot>> GetBudgetsForScopeAsync(
        HttpClient client,
        string scope,
        string scopeType,
        string? scopeDisplayName,
        CancellationToken cancellationToken)
    {
        var budgetUri = $"{BuildAzureManagementUri(scope)}/providers/Microsoft.Consumption/budgets?api-version={UsageDetailsApiVersion}";
        using var response = await client.GetAsync(budgetUri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogInformation("Azure budget sync skipped for scope {Scope}: {StatusCode} {Body}", scope, response.StatusCode, error);
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("value", out var values))
        {
            return [];
        }

        var budgets = new List<ProviderBudgetSnapshot>();
        foreach (var item in values.EnumerateArray())
        {
            var name = GetString(item, "name");
            if (!item.TryGetProperty("properties", out var properties) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var amount = GetDecimal(properties, "amount");
            if (!amount.HasValue)
            {
                continue;
            }

            var notifications = GetNotificationSnapshots(properties);
            var alertThreshold = notifications
                .Where(notification => notification.Enabled && notification.ThresholdPercentage > 0)
                .Select(notification => notification.ThresholdPercentage)
                .DefaultIfEmpty()
                .Min();

            budgets.Add(new ProviderBudgetSnapshot
            {
                Name = name,
                ExternalBudgetId = GetString(item, "id") ?? $"{scope}:{name}",
                Scope = scope,
                ScopeType = scopeType,
                ScopeDisplayName = scopeDisplayName,
                Amount = decimal.Round(amount.Value, 2),
                CurrentSpend = TryGetNestedDecimal(properties, "currentSpend", "amount") is { } currentSpend
                    ? decimal.Round(currentSpend, 2)
                    : null,
                ForecastSpend = TryGetNestedDecimal(properties, "forecastSpend", "amount") is { } forecastSpend
                    ? decimal.Round(forecastSpend, 2)
                    : null,
                Currency = GetString(properties, "currentSpend", "unit")
                    ?? GetString(properties, "amount", "unit")
                    ?? "USD",
                TimeGrain = GetString(properties, "timeGrain") ?? "Monthly",
                AlertThresholdPercentage = alertThreshold > 0 ? alertThreshold : null,
                Notifications = notifications,
                Source = "AzureImported"
            });
        }

        return budgets;
    }

    private static List<ProviderBudgetNotificationSnapshot> GetNotificationSnapshots(JsonElement properties)
    {
        if (!properties.TryGetProperty("notifications", out var notifications) || notifications.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var results = new List<ProviderBudgetNotificationSnapshot>();
        foreach (var notification in notifications.EnumerateObject())
        {
            var threshold = GetDecimal(notification.Value, "threshold");
            if (!threshold.HasValue)
            {
                continue;
            }

            results.Add(new ProviderBudgetNotificationSnapshot
            {
                Name = notification.Name,
                ThresholdPercentage = decimal.ToDouble(threshold.Value),
                ThresholdType = GetString(notification.Value, "thresholdType") ?? "Actual",
                Operator = GetString(notification.Value, "operator") ?? "GreaterThan",
                Enabled = GetBoolean(notification.Value, "enabled") ?? true,
                ContactEmails = GetStringArray(notification.Value, "contactEmails"),
                ContactRoles = GetStringArray(notification.Value, "contactRoles"),
                ContactGroups = GetStringArray(notification.Value, "contactGroups")
            });
        }

        return results
            .OrderBy(notification => notification.ThresholdPercentage)
            .ThenBy(notification => notification.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool? GetBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static List<string> GetStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToList();
    }

    private static string BuildAzureManagementUri(string path) =>
        $"https://management.azure.com{path}";

    private static string BuildQueryString(IReadOnlyDictionary<string, string?> query) =>
        string.Join("&", query
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}"));

    private static Dictionary<string, AzureMetricMapping> ResolveMetricMappings(CloudResource resource)
    {
        var resourceType = resource.Type.Trim().ToLowerInvariant();

        if (resourceType.Contains("microsoft.compute/virtualmachines", StringComparison.Ordinal))
        {
            return new(StringComparer.OrdinalIgnoreCase)
            {
                [NotificationMetricKeys.CpuUtilizationPercent] = new(resource.Id, "Microsoft.Compute/virtualMachines", "Percentage CPU", "average", "average", "Percent", NotificationMetricKeys.CpuUtilizationPercent),
                [NotificationMetricKeys.NetworkInBytes] = new(resource.Id, "Microsoft.Compute/virtualMachines", "Network In Total", "total", "total", "Bytes", NotificationMetricKeys.NetworkInBytes),
                [NotificationMetricKeys.NetworkOutBytes] = new(resource.Id, "Microsoft.Compute/virtualMachines", "Network Out Total", "total", "total", "Bytes", NotificationMetricKeys.NetworkOutBytes),
            };
        }

        if (resourceType.Contains("microsoft.storage/storageaccounts", StringComparison.Ordinal))
        {
            return new(StringComparer.OrdinalIgnoreCase)
            {
                [NotificationMetricKeys.StorageCapacityBytes] = new(resource.Id, "Microsoft.Storage/storageAccounts", "UsedCapacity", "average", "average", "Bytes", NotificationMetricKeys.StorageCapacityBytes),
                [NotificationMetricKeys.StorageTransactionsCount] = new(resource.Id, "Microsoft.Storage/storageAccounts", "Transactions", "total", "total", "Count", NotificationMetricKeys.StorageTransactionsCount),
                [NotificationMetricKeys.NetworkInBytes] = new(resource.Id, "Microsoft.Storage/storageAccounts", "Ingress", "total", "total", "Bytes", NotificationMetricKeys.NetworkInBytes),
                [NotificationMetricKeys.NetworkOutBytes] = new(resource.Id, "Microsoft.Storage/storageAccounts", "Egress", "total", "total", "Bytes", NotificationMetricKeys.NetworkOutBytes),
            };
        }

        if (resourceType.Contains("microsoft.web/sites", StringComparison.Ordinal))
        {
            return new(StringComparer.OrdinalIgnoreCase)
            {
                [NotificationMetricKeys.FunctionInvocationsCount] = new(resource.Id, "Microsoft.Web/sites", "Requests", "total", "total", "Count", NotificationMetricKeys.FunctionInvocationsCount),
                [NotificationMetricKeys.FunctionErrorsCount] = new(resource.Id, "Microsoft.Web/sites", "Http5xx", "total", "total", "Count", NotificationMetricKeys.FunctionErrorsCount),
                [NotificationMetricKeys.FunctionDurationMilliseconds] = new(resource.Id, "Microsoft.Web/sites", "AverageResponseTime", "average", "average", "Milliseconds", NotificationMetricKeys.FunctionDurationMilliseconds),
            };
        }

        return new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record AzureMetricMapping(
        string ResourceUri,
        string MetricNamespace,
        string ProviderMetricName,
        string Aggregation,
        string AggregationProperty,
        string Unit,
        string CanonicalMetricName);
}
