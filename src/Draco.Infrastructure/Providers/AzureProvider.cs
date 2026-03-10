using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Draco.Application.Interfaces;
using Draco.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Draco.Infrastructure.Providers;

public class AzureProvider : ICloudProvider
{
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
                    resources.Add(new CloudResource
                    {
                        Id = resource.Data.Id,
                        Name = resource.Data.Name,
                        Type = resource.Data.ResourceType.ToString(),
                        Provider = ProviderName,
                        Location = resource.Data.Location.Name,
                        SubscriptionId = subscription.Data.SubscriptionId!,
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

    public async Task<IDictionary<string, double>> GetMetricsAsync(string resourceId, IEnumerable<string> metricNames, TimeSpan timespan, string? accessToken = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching metrics for Azure resource {ResourceId}", resourceId);
        // Simplified metrics fetch using Azure.Monitor.Query (Stub for now)
        var metrics = new Dictionary<string, double>();
        foreach (var metric in metricNames)
        {
            metrics[metric] = 0.0; 
        }
        return metrics;
    }

    public async Task<IEnumerable<CostRecommendation>> GetCostRecommendationsAsync(string? accessToken = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Scanning for Azure cost recommendations...");
        var client = GetClient(accessToken);
        var recommendations = new List<CostRecommendation>();

        // Logic to scan for unattached disks, idle VMs, etc.
        // For now, returning empty list (Stub)
        return await Task.FromResult(recommendations);
    }

    public async Task<decimal> GetPriceEstimateAsync(string resourceType, string location, IDictionary<string, string> parameters, string? accessToken = null, CancellationToken cancellationToken = default)
    {
        // Use Azure Retail Prices API or similar
        return 0;
    }

    public async Task<bool> StopResourceAsync(string resourceId, string? accessToken = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping Azure resource {ResourceId}", resourceId);
        var client = GetClient(accessToken);
        try
        {
            var resourceIdentifier = new ResourceIdentifier(resourceId);
            if (resourceIdentifier.ResourceType == "Microsoft.Compute/virtualMachines")
            {
                // Logic to stop VM
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop Azure resource {ResourceId}", resourceId);
            return false;
        }
    }
}
