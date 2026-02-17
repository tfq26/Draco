using Draco.Domain.Entities;

namespace Draco.Domain.Repositories;

public interface IResourceRepository
{
    Task UpsertResourcesAsync(IEnumerable<CloudResource> resources, CancellationToken cancellationToken = default);
    Task<IEnumerable<CloudResource>> GetResourcesAsync(CancellationToken cancellationToken = default);
    Task<CloudResource?> GetResourceByIdAsync(string id, CancellationToken cancellationToken = default);
}
