using Draco.Application.Interfaces;
using Draco.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace Draco.Application.Services;

public class ResourceDiscoveryService
{
    private readonly IEnumerable<ICloudProvider> _providers;
    private readonly IResourceRepository _repository;
    private readonly ILogger<ResourceDiscoveryService> _logger;

    public ResourceDiscoveryService(
        IEnumerable<ICloudProvider> providers,
        IResourceRepository repository,
        ILogger<ResourceDiscoveryService> logger)
    {
        _providers = providers;
        _repository = repository;
        _logger = logger;
    }

    public async Task RunDiscoveryAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting cross-provider resource discovery...");

        foreach (var provider in _providers)
        {
            try
            {
                _logger.LogInformation("Discovering resources for provider: {Provider}", provider.ProviderName);
                var resources = await provider.ListResourcesAsync(cancellationToken);
                
                _logger.LogInformation("Upserting {Count} resources from {Provider} into repository.", resources.Count(), provider.ProviderName);
                await _repository.UpsertResourcesAsync(resources, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during discovery for provider {Provider}.", provider.ProviderName);
            }
        }

        _logger.LogInformation("Resource discovery cycle complete.");
    }
}
