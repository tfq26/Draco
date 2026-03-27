using Draco.Application.Models;

namespace Draco.Application.Interfaces;

public interface IInsightContextService
{
    Task<PreparedInsightContext?> BuildForUserAsync(Guid userId, CancellationToken cancellationToken = default);
    string SerializeForModel(PreparedInsightContext context);
}
