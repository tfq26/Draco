using Azure.Identity;
using Azure.ResourceManager;
using Draco.Application.Interfaces;
using Draco.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Draco.Infrastructure.Providers;

public class AzureProvider : ICloudProvider
{
    private readonly ArmClient _armClient;
    private readonly ILogger<AzureProvider> _logger;

    public string ProviderName => "Azure";

    public AzureProvider(ILogger<AzureProvider> logger)
    {
        _logger = logger;
        // In a real app, we'd pull the credentials from a secure configuration or DefaultAzureCredential
        _armClient = new ArmClient(new DefaultAzureCredential());
    }

    public async Task<IEnumerable<CloudResource>> ListResourcesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting Azure resource discovery...");
        var resources = new List<CloudResource>();

        try
        {
            await foreach (var subscription in _armClient.GetSubscriptions().GetAllAsync(cancellationToken))
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

    public Task<IDictionary<string, double>> GetMetricsAsync(string resourceId, IEnumerable<string> metricNames, TimeSpan timespan, CancellationToken cancellationToken = default)
    {
        // To be implemented with Azure.Monitor.Query
        _logger.LogWarning("GetMetricsAsync not yet implemented for Azure provider.");
        return Task.FromResult<IDictionary<string, double>>(new Dictionary<string, double>());
    }
}
