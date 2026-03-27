using Draco.Application.Models;
using Draco.Domain.Entities;

namespace Draco.Application.Interfaces;

public interface IResourceActionService
{
    Task<IReadOnlyList<ResourceActionDefinition>> GetSupportedActionsAsync(
        CloudResource resource,
        CancellationToken cancellationToken = default);

    Task<ResourceActionExecutionResult> ExecuteAsync(
        UserAccount user,
        CloudConnection connection,
        CloudResource resource,
        string action,
        CancellationToken cancellationToken = default);
}
