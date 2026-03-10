using Draco.Domain.Entities;

namespace Draco.Application.Interfaces;

public interface ICloudProvider
{
    string ProviderName { get; }
    Task<IEnumerable<CloudResource>> ListResourcesAsync(string? accessToken = null, CancellationToken cancellationToken = default);
    Task<IDictionary<string, double>> GetMetricsAsync(string resourceId, IEnumerable<string> metricNames, TimeSpan timespan, string? accessToken = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<CostRecommendation>> GetCostRecommendationsAsync(string? accessToken = null, CancellationToken cancellationToken = default);
    Task<decimal> GetPriceEstimateAsync(string resourceType, string location, IDictionary<string, string> parameters, string? accessToken = null, CancellationToken cancellationToken = default);
    Task<bool> StopResourceAsync(string resourceId, string? accessToken = null, CancellationToken cancellationToken = default);
}
