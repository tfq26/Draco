using Draco.Application.Models;
using Draco.Domain.Entities;

namespace Draco.Application.Interfaces;

public interface ICloudConnectionSyncService
{
    Task<CloudConnectionSyncResult> SyncUserConnectionsAsync(
        UserAccount user,
        IReadOnlyCollection<int>? connectionIds = null,
        CancellationToken cancellationToken = default);
}
