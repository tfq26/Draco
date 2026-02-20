namespace Draco.Application.Interfaces;

public interface IAIService
{
    Task<string> AnalyzeAnomalyAsync(string rawData, CancellationToken cancellationToken = default);
    Task<string> GenerateRemediationHclAsync(string context, CancellationToken cancellationToken = default);
    Task<string> CreateConversationalAlertAsync(string analysis, CancellationToken cancellationToken = default);
    Task<string> ProcessQueryAsync(string query, string context, CancellationToken cancellationToken = default);
}
