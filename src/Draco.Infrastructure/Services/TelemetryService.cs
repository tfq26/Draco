using Draco.Application.Interfaces;
using Draco.Domain.Entities;
using Draco.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Draco.Infrastructure.Services;

public interface ITelemetryService
{
    Task IngestMetricsAsync(IEnumerable<ObservabilityMetric> metrics);
    Task IngestLogsAsync(IEnumerable<ObservabilityLog> logs);
    Task<IEnumerable<ObservabilityMetric>> GetMetricsAsync(string resourceId, string metricName, DateTimeOffset start, DateTimeOffset end);
}

public class TelemetryService : ITelemetryService
{
    private readonly DracoDbContext _dbContext;
    private readonly ILogger<TelemetryService> _logger;

    public TelemetryService(DracoDbContext dbContext, ILogger<TelemetryService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task IngestMetricsAsync(IEnumerable<ObservabilityMetric> metrics)
    {
        _dbContext.ObservabilityMetrics.AddRange(metrics);
        await _dbContext.SaveChangesAsync();
    }

    public async Task IngestLogsAsync(IEnumerable<ObservabilityLog> logs)
    {
        _dbContext.ObservabilityLogs.AddRange(logs);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<IEnumerable<ObservabilityMetric>> GetMetricsAsync(string resourceId, string metricName, DateTimeOffset start, DateTimeOffset end)
    {
        return await _dbContext.ObservabilityMetrics
            .Where(m => m.ResourceId == resourceId && m.MetricName == metricName && m.Timestamp >= start && m.Timestamp <= end)
            .OrderBy(m => m.Timestamp)
            .ToListAsync();
    }
}
