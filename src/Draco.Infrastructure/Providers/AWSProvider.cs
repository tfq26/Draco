using Amazon;
using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
using Amazon.CostExplorer;
using Amazon.CostExplorer.Model;
using Amazon.EC2;
using Amazon.EC2.Model;
using Amazon.Runtime;
using Amazon.Runtime.Internal;
using Amazon.Runtime.Internal.Auth;
using Amazon.Runtime.Internal.Util;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using Draco.Application.Interfaces;
using Draco.Application.Models;
using Draco.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using CloudWatchDimension = Amazon.CloudWatch.Model.Dimension;
using CloudWatchMetric = Amazon.CloudWatch.Model.Metric;

namespace Draco.Infrastructure.Providers;

public class AWSProvider : ICloudProvider
{
    private readonly ILogger<AWSProvider> _logger;
    private readonly IConfiguration _configuration;

    public AWSProvider(ILogger<AWSProvider> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public string ProviderName => "AWS";

    public async Task<IEnumerable<CloudResource>> ListResourcesAsync(string? accessToken = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting AWS resource discovery...");

        var resources = new List<CloudResource>();
        var connectionContext = await ResolveConnectionContextAsync(accessToken, cancellationToken);

        try
        {
            using var regionClient = CreateEc2Client(connectionContext);
            var regions = await regionClient.DescribeRegionsAsync(new DescribeRegionsRequest(), cancellationToken);

            foreach (var region in regions.Regions.Where(region => !string.IsNullOrWhiteSpace(region.RegionName)))
            {
                var regionEndpoint = RegionEndpoint.GetBySystemName(region.RegionName);
                using var ec2Client = CreateEc2Client(connectionContext, regionEndpoint);
                string? nextToken = null;

                do
                {
                    var response = await ec2Client.DescribeInstancesAsync(new DescribeInstancesRequest
                    {
                        NextToken = nextToken
                    }, cancellationToken);

                    foreach (var instance in response.Reservations.SelectMany(reservation => reservation.Instances))
                    {
                        var tags = instance.Tags.ToDictionary(tag => tag.Key, tag => tag.Value);
                        resources.Add(new CloudResource
                        {
                            Id = instance.InstanceId,
                            Name = tags.TryGetValue("Name", out var name) && !string.IsNullOrWhiteSpace(name)
                                ? name
                                : instance.InstanceId,
                            Type = "AWS::EC2::Instance",
                            Provider = ProviderName,
                            Location = region.RegionName!,
                            Tags = tags,
                            DiscoveredAt = DateTimeOffset.UtcNow
                        });
                    }

                    nextToken = response.NextToken;
                }
                while (!string.IsNullOrWhiteSpace(nextToken));
            }

            using var s3Client = CreateS3Client(connectionContext, RegionEndpoint.USEast1);
            var buckets = await s3Client.ListBucketsAsync(cancellationToken);
            foreach (var bucket in buckets.Buckets)
            {
                string location;
                try
                {
                    var locationResponse = await s3Client.GetBucketLocationAsync(new GetBucketLocationRequest
                    {
                        BucketName = bucket.BucketName
                    }, cancellationToken);

                    location = string.IsNullOrWhiteSpace(locationResponse.Location?.Value)
                        ? "us-east-1"
                        : locationResponse.Location.Value;
                }
                catch
                {
                    location = "global";
                }

                resources.Add(new CloudResource
                {
                    Id = $"arn:aws:s3:::{bucket.BucketName}",
                    Name = bucket.BucketName,
                    Type = "AWS::S3::Bucket",
                    Provider = ProviderName,
                    Location = location,
                    Tags = new Dictionary<string, string>(),
                    DiscoveredAt = bucket.CreationDate.HasValue
                        ? new DateTimeOffset(DateTime.SpecifyKind(bucket.CreationDate.Value, DateTimeKind.Utc))
                        : DateTimeOffset.UtcNow
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AWS resource discovery fell back after provider enumeration failed.");
        }

        _logger.LogInformation("Discovered {Count} resources in AWS.", resources.Count);
        return resources;
    }

    public async Task<IEnumerable<ProviderBudgetSnapshot>> GetBudgetsAsync(
        string subscriptionId,
        string? accessToken = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subscriptionId))
        {
            return [];
        }

        try
        {
            using var httpClient = new HttpClient();
            var budgets = new List<ProviderBudgetSnapshot>();
            var connectionContext = await ResolveConnectionContextAsync(accessToken, cancellationToken);
            var config = new AmazonCloudWatchConfig
            {
                AuthenticationRegion = RegionEndpoint.USEast1.SystemName,
                AuthenticationServiceName = "budgets",
                ServiceURL = "https://budgets.amazonaws.com"
            };
            var immutableCredentials = connectionContext.Credentials?.GetCredentials()
                ?? FallbackCredentialsFactory.GetCredentials(config, false).GetCredentials();
            string? nextToken = null;

            do
            {
                var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["AccountId"] = subscriptionId,
                    ["MaxResults"] = 100,
                    ["NextToken"] = nextToken
                });

                using var requestMessage = new HttpRequestMessage(HttpMethod.Post, config.ServiceURL)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/x-amz-json-1.1")
                };
                requestMessage.Headers.TryAddWithoutValidation("X-Amz-Target", "AWSBudgetServiceGateway.DescribeBudgets");

                var signedRequest = new DefaultRequest(new RawAwsRequest(), "budgets")
                {
                    HttpMethod = "POST",
                    Endpoint = new Uri(config.ServiceURL),
                    ResourcePath = "/",
                    Content = Encoding.UTF8.GetBytes(payload)
                };
                signedRequest.Headers["Content-Type"] = "application/x-amz-json-1.1";
                signedRequest.Headers["X-Amz-Target"] = "AWSBudgetServiceGateway.DescribeBudgets";

                if (!string.IsNullOrWhiteSpace(immutableCredentials.Token))
                {
                    signedRequest.Headers["X-Amz-Security-Token"] = immutableCredentials.Token;
                }

                var signer = new AWS4Signer();
                signer.SignRequest(
                    signedRequest,
                    config,
                    new RequestMetrics(),
                    immutableCredentials.AccessKey,
                    immutableCredentials.SecretKey);

                foreach (var header in signedRequest.Headers)
                {
                    if (!requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value))
                    {
                        requestMessage.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }

                using var response = await httpClient.SendAsync(requestMessage, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning("AWS budget sync failed for account {SubscriptionId}: {StatusCode} {Body}", subscriptionId, response.StatusCode, error);
                    return budgets;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                if (!document.RootElement.TryGetProperty("Budgets", out var responseBudgets))
                {
                    return budgets;
                }

                foreach (var budget in responseBudgets.EnumerateArray())
                {
                    var limit = TryGetNestedAmount(budget, "BudgetLimit", "Amount")
                        ?? TryGetPlannedBudgetAmount(budget);

                    if (!limit.HasValue)
                    {
                        continue;
                    }

                    budgets.Add(new ProviderBudgetSnapshot
                    {
                        Name = GetString(budget, "BudgetName") ?? "AWS Budget",
                        ExternalBudgetId = GetString(budget, "BudgetName") ?? Guid.NewGuid().ToString("N"),
                        Scope = subscriptionId,
                        Amount = decimal.Round(limit.Value, 2),
                        CurrentSpend = TryGetNestedAmount(budget, "CalculatedSpend", "ActualSpend", "Amount"),
                        ForecastSpend = TryGetNestedAmount(budget, "CalculatedSpend", "ForecastedSpend", "Amount"),
                        Currency = GetString(budget, "BudgetLimit", "Unit")
                            ?? GetString(budget, "CalculatedSpend", "ActualSpend", "Unit")
                            ?? "USD",
                        TimeGrain = GetString(budget, "TimeUnit") ?? "MONTHLY",
                        AlertThresholdPercentage = 80,
                        Source = "AwsImported"
                    });
                }

                nextToken = document.RootElement.TryGetProperty("NextToken", out var nextTokenElement)
                    ? nextTokenElement.GetString()
                    : null;
            }
            while (!string.IsNullOrWhiteSpace(nextToken));

            return budgets;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AWS budget sync failed for account {SubscriptionId}", subscriptionId);
            return [];
        }
    }

    public async Task<IEnumerable<CloudResourceCost>> GetResourceCostsAsync(
        string subscriptionId,
        IEnumerable<CloudResource> resources,
        string? accessToken = null,
        CancellationToken cancellationToken = default)
    {
        var resourceList = resources.ToList();
        if (resourceList.Count == 0)
        {
            return [];
        }

        try
        {
            using var client = CreateCostExplorerClient(await ResolveConnectionContextAsync(accessToken, cancellationToken));
            var periodStart = new DateTimeOffset(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
            var periodEnd = periodStart.AddMonths(1);
            var request = new GetCostAndUsageWithResourcesRequest
            {
                Granularity = Granularity.MONTHLY,
                TimePeriod = new DateInterval
                {
                    Start = periodStart.UtcDateTime.ToString("yyyy-MM-dd"),
                    End = periodEnd.UtcDateTime.ToString("yyyy-MM-dd")
                },
                Metrics = ["UnblendedCost"],
                GroupBy =
                [
                    new GroupDefinition
                    {
                        Type = GroupDefinitionType.DIMENSION,
                        Key = "RESOURCE_ID"
                    }
                ],
                Filter = new Expression
                {
                    And =
                    [
                        new Expression
                        {
                            Dimensions = new DimensionValues
                            {
                                Key = Amazon.CostExplorer.Dimension.SERVICE,
                                Values = ["Amazon Elastic Compute Cloud - Compute"]
                            }
                        },
                        new Expression
                        {
                            Dimensions = new DimensionValues
                            {
                                Key = Amazon.CostExplorer.Dimension.LINKED_ACCOUNT,
                                Values = [subscriptionId]
                            }
                        }
                    ]
                }
            };

            var resourceLookup = resourceList.ToDictionary(
                resource => NormalizeAwsResourceId(resource.Id),
                resource => resource,
                StringComparer.OrdinalIgnoreCase);
            var actualCosts = new Dictionary<string, CloudResourceCost>(StringComparer.OrdinalIgnoreCase);

            do
            {
                var response = await client.GetCostAndUsageWithResourcesAsync(request, cancellationToken);
                foreach (var result in response.ResultsByTime)
                {
                    foreach (var group in result.Groups)
                    {
                        var groupedResourceId = group.Keys.FirstOrDefault();
                        if (string.IsNullOrWhiteSpace(groupedResourceId))
                        {
                            continue;
                        }

                        var normalizedId = NormalizeAwsResourceId(groupedResourceId);
                        if (!resourceLookup.TryGetValue(normalizedId, out var resource))
                        {
                            continue;
                        }

                        if (!group.Metrics.TryGetValue("UnblendedCost", out var metric) ||
                            !decimal.TryParse(metric.Amount, out var amount))
                        {
                            continue;
                        }

                        actualCosts[resource.Id] = new CloudResourceCost
                        {
                            ResourceId = resource.Id,
                            Provider = ProviderName,
                            SubscriptionId = subscriptionId,
                            ResourceGroupName = string.Empty,
                            Amount = decimal.Round(amount, 2),
                            Currency = string.IsNullOrWhiteSpace(metric.Unit) ? "USD" : metric.Unit,
                            Granularity = "Monthly",
                            PeriodStart = periodStart,
                            PeriodEnd = DateTimeOffset.UtcNow,
                            CapturedAt = DateTimeOffset.UtcNow,
                            CostSource = "AwsActual"
                        };
                    }
                }

                request.NextPageToken = response.NextPageToken;
            }
            while (!string.IsNullOrWhiteSpace(request.NextPageToken));

            return actualCosts.Values.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AWS actual resource cost ingestion failed for subscription/account {SubscriptionId}. Falling back to estimates.", subscriptionId);
            return [];
        }
    }

    public async Task<IEnumerable<ObservabilityMetric>> GetMetricsAsync(
        CloudResource resource,
        IEnumerable<string> metricNames,
        TimeSpan timespan,
        string? accessToken = null,
        CancellationToken cancellationToken = default)
    {
        var mappings = ResolveMetricMappings(resource);
        var requestedMappings = metricNames
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(metricName => mappings.TryGetValue(metricName, out var mapping) ? mapping : null)
            .Where(mapping => mapping is not null)
            .Cast<AwsMetricMapping>()
            .ToList();

        if (requestedMappings.Count == 0)
        {
            return [];
        }

        try
        {
            var connectionContext = await ResolveConnectionContextAsync(accessToken, cancellationToken);
            using var client = CreateCloudWatchClient(connectionContext, ResolveRegion(resource, connectionContext.Region));
            var endTime = DateTime.UtcNow;
            var startTime = endTime.Subtract(timespan);
            var request = new GetMetricDataRequest
            {
                StartTime = startTime,
                EndTime = endTime,
                MetricDataQueries = requestedMappings.Select((mapping, index) => new MetricDataQuery
                {
                    Id = $"m{index}",
                    ReturnData = true,
                    MetricStat = new MetricStat
                    {
                        Period = (int)Math.Max(300, Math.Min(timespan.TotalSeconds, 3600)),
                        Stat = mapping.Stat,
                        Metric = new CloudWatchMetric
                        {
                            Namespace = mapping.Namespace,
                            MetricName = mapping.ProviderMetricName,
                            Dimensions = mapping.Dimensions
                                .Select(item => new CloudWatchDimension { Name = item.Key, Value = item.Value })
                                .ToList()
                        }
                    }
                }).ToList()
            };

            var response = await client.GetMetricDataAsync(request, cancellationToken);
            var results = new List<ObservabilityMetric>();
            for (var index = 0; index < requestedMappings.Count; index++)
            {
                var mapping = requestedMappings[index];
                var metricResult = response.MetricDataResults.FirstOrDefault(result => result.Id == $"m{index}");
                if (metricResult?.Values is null || metricResult.Values.Count == 0)
                {
                    continue;
                }

                var latestIndex = metricResult.Values.Count - 1;
                results.Add(new ObservabilityMetric
                {
                    ResourceId = resource.Id,
                    MetricName = mapping.CanonicalMetricName,
                    Value = metricResult.Values[latestIndex],
                    Unit = mapping.Unit,
                    Timestamp = metricResult.Timestamps.Count > latestIndex
                        ? new DateTimeOffset(metricResult.Timestamps[latestIndex], TimeSpan.Zero)
                        : DateTimeOffset.UtcNow,
                    Dimensions = mapping.Dimensions
                });
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "AWS metrics request failed for resource {ResourceId}", resource.Id);
            return [];
        }
    }

    public Task<IEnumerable<CostRecommendation>> GetCostRecommendationsAsync(string? accessToken = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Scanning for AWS cost recommendations...");
        return Task.FromResult<IEnumerable<CostRecommendation>>([]);
    }

    public Task<decimal> GetPriceEstimateAsync(string resourceType, string location, IDictionary<string, string> parameters, string? accessToken = null, CancellationToken cancellationToken = default)
    {
        var normalizedType = resourceType.Trim().ToLowerInvariant();

        var estimate = normalizedType switch
        {
            var value when value.Contains("ec2::instance", StringComparison.Ordinal) => 110m,
            var value when value.Contains("rds::dbinstance", StringComparison.Ordinal) => 145m,
            var value when value.Contains("s3::bucket", StringComparison.Ordinal) => 15m,
            var value when value.Contains("eks::cluster", StringComparison.Ordinal) => 180m,
            var value when value.Contains("elasticloadbalancing", StringComparison.Ordinal) => 24m,
            var value when value.Contains("lambda::function", StringComparison.Ordinal) => 8m,
            _ => 12m,
        };

        return Task.FromResult(estimate);
    }

    public Task<bool> StopResourceAsync(string resourceId, string? accessToken = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping AWS resource {ResourceId}", resourceId);
        return Task.FromResult(true);
    }

    private static string NormalizeAwsResourceId(string resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return string.Empty;
        }

        var trimmed = resourceId.Trim();
        var slashIndex = trimmed.LastIndexOf('/');
        return slashIndex >= 0 ? trimmed[(slashIndex + 1)..] : trimmed;
    }

    private static decimal? ParseAmount(string? amount) =>
        decimal.TryParse(amount, out var parsed) ? parsed : null;

    private static decimal? TryGetNestedAmount(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var segment in path)
        {
            if (!current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind == JsonValueKind.String
            ? ParseAmount(current.GetString())
            : current.ValueKind == JsonValueKind.Number && current.TryGetDecimal(out var numeric)
                ? numeric
                : null;
    }

    private static decimal? TryGetPlannedBudgetAmount(JsonElement budget)
    {
        if (!budget.TryGetProperty("PlannedBudgetLimits", out var limits) || limits.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var item in limits.EnumerateObject())
        {
            var amount = TryGetNestedAmount(item.Value, "Amount");
            if (amount.HasValue)
            {
                return amount;
            }
        }

        return null;
    }

    private static string? GetString(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var segment in path)
        {
            if (!current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind == JsonValueKind.String
            ? current.GetString()
            : null;
    }

    private static Dictionary<string, AwsMetricMapping> ResolveMetricMappings(CloudResource resource)
    {
        var resourceType = resource.Type.Trim().ToLowerInvariant();

        if (resourceType.Contains("aws::ec2::instance", StringComparison.Ordinal))
        {
            var instanceId = NormalizeAwsResourceId(resource.Id);
            return new(StringComparer.OrdinalIgnoreCase)
            {
                [NotificationMetricKeys.CpuUtilizationPercent] = new("AWS/EC2", "CPUUtilization", "Average", "Percent", NotificationMetricKeys.CpuUtilizationPercent, new Dictionary<string, string> { ["InstanceId"] = instanceId }),
                [NotificationMetricKeys.MemoryUtilizationPercent] = new("CWAgent", "mem_used_percent", "Average", "Percent", NotificationMetricKeys.MemoryUtilizationPercent, new Dictionary<string, string> { ["InstanceId"] = instanceId }),
                [NotificationMetricKeys.NetworkInBytes] = new("AWS/EC2", "NetworkIn", "Sum", "Bytes", NotificationMetricKeys.NetworkInBytes, new Dictionary<string, string> { ["InstanceId"] = instanceId }),
                [NotificationMetricKeys.NetworkOutBytes] = new("AWS/EC2", "NetworkOut", "Sum", "Bytes", NotificationMetricKeys.NetworkOutBytes, new Dictionary<string, string> { ["InstanceId"] = instanceId }),
            };
        }

        if (resourceType.Contains("aws::s3::bucket", StringComparison.Ordinal))
        {
            var bucketName = resource.Name;
            return new(StringComparer.OrdinalIgnoreCase)
            {
                [NotificationMetricKeys.StorageCapacityBytes] = new("AWS/S3", "BucketSizeBytes", "Average", "Bytes", NotificationMetricKeys.StorageCapacityBytes, new Dictionary<string, string> { ["BucketName"] = bucketName, ["StorageType"] = "StandardStorage" }),
                [NotificationMetricKeys.StorageObjectCount] = new("AWS/S3", "NumberOfObjects", "Average", "Count", NotificationMetricKeys.StorageObjectCount, new Dictionary<string, string> { ["BucketName"] = bucketName, ["StorageType"] = "AllStorageTypes" }),
            };
        }

        if (resourceType.Contains("aws::lambda::function", StringComparison.Ordinal))
        {
            var functionName = resource.Id.Split(':', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? resource.Name;
            return new(StringComparer.OrdinalIgnoreCase)
            {
                [NotificationMetricKeys.FunctionInvocationsCount] = new("AWS/Lambda", "Invocations", "Sum", "Count", NotificationMetricKeys.FunctionInvocationsCount, new Dictionary<string, string> { ["FunctionName"] = functionName }),
                [NotificationMetricKeys.FunctionErrorsCount] = new("AWS/Lambda", "Errors", "Sum", "Count", NotificationMetricKeys.FunctionErrorsCount, new Dictionary<string, string> { ["FunctionName"] = functionName }),
                [NotificationMetricKeys.FunctionDurationMilliseconds] = new("AWS/Lambda", "Duration", "Average", "Milliseconds", NotificationMetricKeys.FunctionDurationMilliseconds, new Dictionary<string, string> { ["FunctionName"] = functionName }),
            };
        }

        return new(StringComparer.OrdinalIgnoreCase);
    }

    private static RegionEndpoint ResolveRegion(CloudResource resource, RegionEndpoint fallbackRegion)
    {
        if (!string.IsNullOrWhiteSpace(resource.Location))
        {
            return RegionEndpoint.GetBySystemName(resource.Location);
        }

        var parts = resource.Id.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 4 && !string.IsNullOrWhiteSpace(parts[3]))
        {
            return RegionEndpoint.GetBySystemName(parts[3]);
        }

        return fallbackRegion;
    }

    private async Task<AwsConnectionContext> ResolveConnectionContextAsync(string? accessToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return new AwsConnectionContext(null, RegionEndpoint.USEast1, null);
        }

        var payload = ParseConnectionPayload(accessToken);
        if (payload is null)
        {
            throw new InvalidOperationException("AWS connection payload is invalid. Reconnect this account using Draco's Assume Role or Access Keys flow.");
        }

        var region = !string.IsNullOrWhiteSpace(payload.Region)
            ? RegionEndpoint.GetBySystemName(payload.Region.Trim())
            : RegionEndpoint.USEast1;

        if (string.Equals(payload.Kind, "AwsAssumeRole", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(payload.RoleArn) || string.IsNullOrWhiteSpace(payload.ExternalId))
            {
                throw new InvalidOperationException("AWS role-based connection is missing a role ARN or external ID.");
            }

            try
            {
                using var stsClient = CreateStsClient(region);
                var response = await stsClient.AssumeRoleAsync(new AssumeRoleRequest
                {
                    RoleArn = payload.RoleArn.Trim(),
                    ExternalId = payload.ExternalId.Trim(),
                    RoleSessionName = string.IsNullOrWhiteSpace(payload.RoleSessionName)
                        ? $"draco-{Guid.NewGuid():N}"[..20]
                        : payload.RoleSessionName.Trim(),
                    DurationSeconds = 3600
                }, cancellationToken);

                return new AwsConnectionContext(
                    new SessionAWSCredentials(
                        response.Credentials.AccessKeyId,
                        response.Credentials.SecretAccessKey,
                        response.Credentials.SessionToken),
                    region,
                    payload);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"AWS AssumeRole failed for {payload.RoleArn.Trim()}. Verify the Terraform-created trust policy, external ID, and Draco AWS principal.",
                    ex);
            }
        }

        if (!string.Equals(payload.Kind, "AwsStaticCredentials", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("AWS connection payload kind must be AwsAssumeRole or AwsStaticCredentials.");
        }

        if (string.IsNullOrWhiteSpace(payload.AccessKeyId) || string.IsNullOrWhiteSpace(payload.SecretAccessKey))
        {
            throw new InvalidOperationException("AWS credential-based connection is missing an access key ID or secret access key.");
        }

        AWSCredentials credentials = string.IsNullOrWhiteSpace(payload.SessionToken)
            ? new BasicAWSCredentials(payload.AccessKeyId.Trim(), payload.SecretAccessKey.Trim())
            : new SessionAWSCredentials(payload.AccessKeyId.Trim(), payload.SecretAccessKey.Trim(), payload.SessionToken.Trim());

        return new AwsConnectionContext(credentials, region, payload);
    }

    private AwsCredentialPayload? ParseConnectionPayload(string? accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken) || !accessToken.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<AwsCredentialPayload>(accessToken, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true
            });

            return payload;
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "AWS connection token payload was not valid JSON credentials.");
            return null;
        }
    }

    private static AmazonEC2Client CreateEc2Client(AwsConnectionContext context, RegionEndpoint? region = null) =>
        context.Credentials is null
            ? new AmazonEC2Client(region ?? context.Region)
            : new AmazonEC2Client(context.Credentials, region ?? context.Region);

    private static AmazonS3Client CreateS3Client(AwsConnectionContext context, RegionEndpoint region) =>
        context.Credentials is null
            ? new AmazonS3Client(region)
            : new AmazonS3Client(context.Credentials, region);

    private static AmazonCostExplorerClient CreateCostExplorerClient(AwsConnectionContext context) =>
        context.Credentials is null
            ? new AmazonCostExplorerClient(RegionEndpoint.USEast1)
            : new AmazonCostExplorerClient(context.Credentials, RegionEndpoint.USEast1);

    private static AmazonCloudWatchClient CreateCloudWatchClient(AwsConnectionContext context, RegionEndpoint region) =>
        context.Credentials is null
            ? new AmazonCloudWatchClient(region)
            : new AmazonCloudWatchClient(context.Credentials, region);

    private static AmazonSecurityTokenServiceClient CreateStsClient(RegionEndpoint region) =>
        new(region);

    private sealed record AwsMetricMapping(
        string Namespace,
        string ProviderMetricName,
        string Stat,
        string Unit,
        string CanonicalMetricName,
        Dictionary<string, string> Dimensions);

    private sealed record AwsConnectionContext(
        AWSCredentials? Credentials,
        RegionEndpoint Region,
        AwsCredentialPayload? Payload);

    private sealed class AwsCredentialPayload
    {
        public string? Kind { get; set; }
        public string? AccessKeyId { get; set; }
        public string? SecretAccessKey { get; set; }
        public string? SessionToken { get; set; }
        public string? RoleArn { get; set; }
        public string? ExternalId { get; set; }
        public string? RoleSessionName { get; set; }
        public string? Region { get; set; }
    }

    private sealed class RawAwsRequest : AmazonWebServiceRequest
    {
    }
}
