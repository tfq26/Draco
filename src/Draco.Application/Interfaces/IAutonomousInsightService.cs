using Draco.Application.Models;

namespace Draco.Application.Interfaces;

public interface IAutonomousInsightService
{
    Task<AutonomousInsightResponse?> AnswerUserQueryAsync(Guid userId, string query, CancellationToken cancellationToken = default);
}
