using Draco.Domain.Entities;
using Draco.Domain.Repositories;
using Draco.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Draco.Infrastructure.Repositories;

public class ResourceRepository : IResourceRepository
{
    private readonly DracoDbContext _dbContext;

    public ResourceRepository(DracoDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task UpsertResourcesAsync(IEnumerable<CloudResource> resources, CancellationToken cancellationToken = default)
    {
        foreach (var resource in resources)
        {
            var existing = await _dbContext.CloudResources
                .FirstOrDefaultAsync(r => r.Id == resource.Id, cancellationToken);

            if (existing == null)
            {
                await _dbContext.CloudResources.AddAsync(resource, cancellationToken);
            }
            else
            {
                // Update properties
                _dbContext.Entry(existing).CurrentValues.SetValues(resource);
                existing.DiscoveredAt = DateTimeOffset.UtcNow;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IEnumerable<CloudResource>> GetResourcesAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.CloudResources.ToListAsync(cancellationToken);
    }

    public async Task<CloudResource?> GetResourceByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.CloudResources.FindAsync(new object[] { id }, cancellationToken);
    }
}
