using Draco.Domain.Entities;

namespace Draco.Application.Interfaces;

public interface ICloudProvider
{
    string ProviderName { get; }
    Task<IEnumerable<CloudResource>> ListResourcesAsync(CancellationToken cancellationToken = default);
    Task<IDictionary<string, double>> GetMetricsAsync(string resourceId, IEnumerable<string> metricNames, TimeSpan timespan, CancellationToken cancellationToken = default);
}
