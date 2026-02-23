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

    public Task<IDictionary<string, double>> GetMetricsAsync(string resourceId, IEnumerable<string> metricNames, TimeSpan timespan, string? accessToken = null, CancellationToken cancellationToken = default)
    {
        // To be implemented with Azure.Monitor.Query
        _logger.LogWarning("GetMetricsAsync not yet implemented for Azure provider.");
        return Task.FromResult<IDictionary<string, double>>(new Dictionary<string, double>());
    }
}
