namespace Draco.Domain.Entities;

public class ObservabilityMetric
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ResourceId { get; set; } = string.Empty;
    public string MetricName { get; set; } = string.Empty; // e.g., CPUUtilization, MemoryUsage
    public double Value { get; set; }
    public string Unit { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public IDictionary<string, string> Dimensions { get; set; } = new Dictionary<string, string>();
}
