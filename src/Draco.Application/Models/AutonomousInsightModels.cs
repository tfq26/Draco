namespace Draco.Application.Models;

public sealed class AutonomousInsightResponse
{
    public string Query { get; init; } = string.Empty;
    public string FocusArea { get; init; } = "General";
    public string ApprovalPolicy { get; init; } = "All actions require explicit user review and approval before execution.";
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
    public InsightOverview Overview { get; init; } = new();
    public IReadOnlyList<AutonomousResourceObservation> ResourcesInScope { get; init; } = [];
    public IReadOnlyList<string> Findings { get; init; } = [];
    public IReadOnlyList<AutonomousActionProposal> ProposedActions { get; init; } = [];
    public IReadOnlyList<AutonomousWorkflowProposal> SuggestedWorkflows { get; init; } = [];
    public string Narrative { get; init; } = string.Empty;
}

public sealed class AutonomousResourceObservation
{
    public string ResourceId { get; init; } = string.Empty;
    public string ResourceName { get; init; } = string.Empty;
    public string ResourceType { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public string SubscriptionId { get; init; } = string.Empty;
    public string ResourceGroupName { get; init; } = string.Empty;
    public decimal? MonthlyCost { get; init; }
    public string Currency { get; init; } = "USD";
    public string CostSource { get; init; } = "Unavailable";
    public string? Recommendation { get; init; }
    public string? RecommendationType { get; init; }
    public decimal? PotentialSavings { get; init; }
}

public sealed class AutonomousActionProposal
{
    public string ResourceId { get; init; } = string.Empty;
    public string ResourceName { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public bool IsDestructive { get; init; }
    public bool ApprovalRequired { get; init; } = true;
}

public sealed class AutonomousWorkflowProposal
{
    public string Id { get; init; } = string.Empty;
    public string Trigger { get; init; } = string.Empty;
    public string SuggestedAction { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string? ResourceId { get; init; }
    public string Provider { get; init; } = string.Empty;
    public bool ApprovalRequired { get; init; } = true;
}
