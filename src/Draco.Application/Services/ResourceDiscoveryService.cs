using Draco.Application.Interfaces;
using Draco.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Draco.Application.Services;

public class ResourceDiscoveryService
{
    private readonly IEnumerable<ICloudProvider> _providers;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ResourceDiscoveryService> _logger;

    public ResourceDiscoveryService(
        IEnumerable<ICloudProvider> providers,
        IServiceScopeFactory scopeFactory,
        ILogger<ResourceDiscoveryService> logger)
    {
        _providers = providers;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task RunDiscoveryAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting cross-provider resource discovery...");

        var tasks = _providers.Select(async provider =>
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IResourceRepository>();
            
            try
            {
                _logger.LogInformation("Discovering resources for provider: {Provider}", provider.ProviderName);
                var resources = await provider.ListResourcesAsync(null, cancellationToken);
                
                _logger.LogInformation("Upserting {Count} resources from {Provider} into repository.", resources.Count(), provider.ProviderName);
                await repository.UpsertResourcesAsync(resources, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during discovery for provider {Provider}.", provider.ProviderName);
            }
        });

        await Task.WhenAll(tasks);

        _logger.LogInformation("Resource discovery cycle complete.");
    }
}
