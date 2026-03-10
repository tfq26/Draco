namespace Draco.Domain.Entities;

public class ObservabilityLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ResourceId { get; set; } = string.Empty;
    public string Level { get; set; } = "Information"; // Information, Warning, Error, Critical
    public string Message { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty; // e.g., AzureActivity, SystemEvents
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string? RawData { get; set; } // JSON blob of the log entry
}
