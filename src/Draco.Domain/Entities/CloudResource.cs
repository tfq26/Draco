namespace Draco.Domain.Entities;

public class CloudResource
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // e.g., Microsoft.Compute/virtualMachines
    public string Provider { get; set; } = string.Empty; // e.g., Azure, AWS
    public string Location { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
    public string ResourceGroupName { get; set; } = string.Empty;
    public IDictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();
    public DateTimeOffset DiscoveredAt { get; set; } = DateTimeOffset.UtcNow;
    public string? RawMetadata { get; set; } // JSON blob of the provider's response
}
