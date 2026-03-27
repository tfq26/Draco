namespace Draco.Application.Models;

public sealed class ResourceActionDefinition
{
    public string Action { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool IsDestructive { get; init; }
    public string Provider { get; init; } = string.Empty;
    public string ResourceType { get; init; } = string.Empty;
    public string ExecutionMode { get; init; } = "Terraform";
}

public sealed class ResourceActionExecutionResult
{
    public Guid AuditId { get; init; }
    public string Action { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string WorkspacePath { get; init; } = string.Empty;
    public string TerraformConfiguration { get; init; } = string.Empty;
    public string? Output { get; init; }
    public string? ErrorOutput { get; init; }
    public string? ResponseBody { get; init; }
    public int? ResponseStatusCode { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? ErrorMessage { get; init; }
}
