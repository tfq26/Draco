using System.Text.Json;
using Draco.Application.Interfaces;
using Draco.Application.Models;
using Draco.Domain.Entities;
using Draco.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Draco.Infrastructure.Services;

public sealed class AutonomousInsightService : IAutonomousInsightService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly DracoDbContext _dbContext;
    private readonly IInsightContextService _insightContextService;
    private readonly IAIService _aiService;
    private readonly IResourceActionService _resourceActionService;
    private readonly ILogger<AutonomousInsightService> _logger;

    public AutonomousInsightService(
        DracoDbContext dbContext,
        IInsightContextService insightContextService,
        IAIService aiService,
        IResourceActionService resourceActionService,
        ILogger<AutonomousInsightService> logger)
    {
        _dbContext = dbContext;
        _insightContextService = insightContextService;
        _aiService = aiService;
        _resourceActionService = resourceActionService;
        _logger = logger;
    }

    public async Task<AutonomousInsightResponse?> AnswerUserQueryAsync(Guid userId, string query, CancellationToken cancellationToken = default)
    {
        var trimmedQuery = query.Trim();
        var context = await _insightContextService.BuildForUserAsync(userId, cancellationToken);
        if (context is null)
        {
            return null;
        }

        var user = await _dbContext.UserAccounts
            .AsNoTracking()
            .Include(account => account.Connections)
            .FirstOrDefaultAsync(account => account.Id == userId, cancellationToken);

        if (user is null)
        {
            return null;
        }

        var allowedSubscriptions = user.Connections
            .Where(connection => connection.IsActive)
            .Select(connection => connection.SubscriptionId)
            .Where(subscriptionId => !string.IsNullOrWhiteSpace(subscriptionId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var resources = allowedSubscriptions.Count == 0
            ? []
            : await _dbContext.CloudResources
                .AsNoTracking()
                .Where(resource => allowedSubscriptions.Contains(resource.SubscriptionId))
                .ToListAsync(cancellationToken);

        var focusArea = DetermineFocusArea(trimmedQuery);
        var filteredResources = FilterResources(resources, trimmedQuery, focusArea);
        if (filteredResources.Count == 0)
        {
            filteredResources = resources
                .OrderBy(resource => resource.Provider)
                .ThenBy(resource => resource.Name)
                .Take(12)
                .ToList();
        }

        var resourceIds = filteredResources
            .Select(resource => resource.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var latestCosts = resourceIds.Count == 0
            ? []
            : await _dbContext.CloudResourceCosts
                .AsNoTracking()
                .Where(cost => cost.UserId == userId && resourceIds.Contains(cost.ResourceId))
                .OrderByDescending(cost => cost.PeriodEnd)
                .ThenByDescending(cost => cost.CapturedAt)
                .ToListAsync(cancellationToken);

        var costLookup = latestCosts
            .GroupBy(cost => cost.ResourceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var recommendations = resourceIds.Count == 0
            ? []
            : await _dbContext.CostRecommendations
                .AsNoTracking()
                .Where(item => resourceIds.Contains(item.ResourceId))
                .OrderByDescending(item => item.PotentialSavings)
                .ToListAsync(cancellationToken);

        var recommendationLookup = recommendations
            .GroupBy(item => item.ResourceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var scopedAnomalies = context.Anomalies
            .Where(anomaly => IsInScope(anomaly, focusArea, resourceIds, trimmedQuery))
            .Take(8)
            .ToList();

        var scopedWorkflows = context.WorkflowSuggestions
            .Where(workflow => IsInScope(workflow, focusArea, resourceIds, trimmedQuery))
            .Take(8)
            .ToList();

        var observations = filteredResources
            .Take(12)
            .Select(resource =>
            {
                costLookup.TryGetValue(resource.Id, out var cost);
                recommendationLookup.TryGetValue(resource.Id, out var recommendation);

                return new AutonomousResourceObservation
                {
                    ResourceId = resource.Id,
                    ResourceName = resource.Name,
                    ResourceType = resource.Type,
                    Provider = resource.Provider,
                    Location = resource.Location,
                    SubscriptionId = resource.SubscriptionId,
                    ResourceGroupName = resource.ResourceGroupName,
                    MonthlyCost = cost?.Amount,
                    Currency = cost?.Currency ?? "USD",
                    CostSource = cost?.CostSource ?? "Unavailable",
                    Recommendation = recommendation?.Description,
                    RecommendationType = recommendation?.RecommendationType,
                    PotentialSavings = recommendation?.PotentialSavings
                };
            })
            .OrderByDescending(item => item.MonthlyCost ?? 0m)
            .ThenBy(item => item.ResourceName)
            .ToList();

        var findings = BuildFindings(focusArea, context, observations, scopedAnomalies, recommendations);
        var proposedActions = await BuildActionProposalsAsync(
            filteredResources,
            recommendationLookup,
            trimmedQuery,
            cancellationToken);

        var workflowProposals = scopedWorkflows
            .Select(workflow => new AutonomousWorkflowProposal
            {
                Id = workflow.Id,
                Trigger = workflow.Trigger,
                SuggestedAction = workflow.SuggestedAction,
                Severity = workflow.Severity,
                Reason = workflow.Reason,
                ResourceId = workflow.ResourceId,
                Provider = workflow.Provider,
                ApprovalRequired = true
            })
            .ToList();

        var narrativeContext = new
        {
            approvalPolicy = "All actions must be explicitly approved by the user before execution. Do not imply anything has already been changed.",
            focusArea,
            query = trimmedQuery,
            overview = context.Overview,
            scope = new
            {
                matchedResourceCount = filteredResources.Count,
                matchedResources = observations,
                matchingAnomalies = scopedAnomalies,
                matchingRecommendations = recommendations.Take(8),
                matchingWorkflows = workflowProposals
            },
            findings,
            globalContext = new
            {
                providerBreakdown = context.ProviderBreakdown,
                resourceTypeBreakdown = context.ResourceTypeBreakdown.Take(10),
                providerCostBreakdown = context.ProviderCostBreakdown,
                budgets = context.Budgets
            }
        };

        var narrativePrompt = """
Provide a grounded operator-style answer to the user's question.
Speak naturally, like an experienced engineer reviewing the environment for them.
Lead with what you are seeing, then call out anything notable, then mention action ideas only as proposals.
Every action must be framed as requiring explicit user approval before execution.
If storage or another resource category is in scope, talk through the actual resources in that category instead of giving generic advice.
Default to a short answer: 2-4 sentences for most questions.
Only expand beyond that when the user explicitly asks for more detail or when extra explanation is needed to avoid a misleading answer.
Prefer one compact paragraph over lists unless the content is inherently list-shaped.
Prefer plain language over flourish. Do not use emojis unless the user explicitly asks for them.
For cost questions, explain the difference between actual spend, budget forecast, and estimated totals before proposing any action.
""";

        var narrative = await _aiService.ProcessQueryAsync(
            $"{trimmedQuery}\n\n{narrativePrompt}",
            JsonSerializer.Serialize(narrativeContext, JsonOptions),
            cancellationToken);

        return new AutonomousInsightResponse
        {
            Query = trimmedQuery,
            FocusArea = focusArea,
            GeneratedAt = DateTimeOffset.UtcNow,
            Overview = context.Overview,
            ResourcesInScope = observations,
            Findings = findings,
            ProposedActions = proposedActions,
            SuggestedWorkflows = workflowProposals,
            Narrative = narrative
        };
    }

    private async Task<IReadOnlyList<AutonomousActionProposal>> BuildActionProposalsAsync(
        IReadOnlyCollection<CloudResource> resources,
        IReadOnlyDictionary<string, CostRecommendation> recommendationLookup,
        string query,
        CancellationToken cancellationToken)
    {
        var proposals = new List<AutonomousActionProposal>();
        var explicitDeleteIntent = query.Contains("delete", StringComparison.OrdinalIgnoreCase) ||
                                   query.Contains("remove", StringComparison.OrdinalIgnoreCase);

        foreach (var resource in resources.Take(8))
        {
            var actions = await _resourceActionService.GetSupportedActionsAsync(resource, cancellationToken);
            if (actions.Count == 0)
            {
                continue;
            }

            recommendationLookup.TryGetValue(resource.Id, out var recommendation);
            var selectedAction = SelectAction(actions, recommendation, explicitDeleteIntent);
            if (selectedAction is null)
            {
                continue;
            }

            var reason = recommendation is not null
                ? $"{recommendation.Description} Potential monthly savings: {recommendation.PotentialSavings:0.##} {recommendation.Currency}."
                : $"This resource is in scope for the current request and supports a '{selectedAction.Label}' action.";

            proposals.Add(new AutonomousActionProposal
            {
                ResourceId = resource.Id,
                ResourceName = resource.Name,
                Provider = resource.Provider,
                Action = selectedAction.Action,
                Label = selectedAction.Label,
                Description = selectedAction.Description,
                Reason = reason,
                IsDestructive = selectedAction.IsDestructive,
                ApprovalRequired = true
            });
        }

        return proposals
            .GroupBy(item => $"{item.ResourceId}:{item.Action}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(5)
            .ToList();
    }

    private static ResourceActionDefinition? SelectAction(
        IReadOnlyList<ResourceActionDefinition> actions,
        CostRecommendation? recommendation,
        bool explicitDeleteIntent)
    {
        if (explicitDeleteIntent)
        {
            return actions.FirstOrDefault(action => action.Action.Equals("delete", StringComparison.OrdinalIgnoreCase));
        }

        if (recommendation is not null &&
            (recommendation.Description.Contains("idle", StringComparison.OrdinalIgnoreCase) ||
             recommendation.Description.Contains("unused", StringComparison.OrdinalIgnoreCase) ||
             recommendation.Description.Contains("underutilized", StringComparison.OrdinalIgnoreCase)))
        {
            return actions.FirstOrDefault(action => action.Action.Equals("pause", StringComparison.OrdinalIgnoreCase))
                   ?? actions.FirstOrDefault(action => action.Action.Equals("restart", StringComparison.OrdinalIgnoreCase));
        }

        return actions.FirstOrDefault(action => action.Action.Equals("pause", StringComparison.OrdinalIgnoreCase))
               ?? actions.FirstOrDefault(action => action.Action.Equals("restart", StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> BuildFindings(
        string focusArea,
        PreparedInsightContext context,
        IReadOnlyCollection<AutonomousResourceObservation> observations,
        IReadOnlyCollection<InsightAnomaly> anomalies,
        IReadOnlyCollection<CostRecommendation> recommendations)
    {
        var findings = new List<string>
        {
            $"{focusArea} scope includes {observations.Count} matched resources across {observations.Select(item => item.Provider).Distinct(StringComparer.OrdinalIgnoreCase).Count()} provider(s).",
            $"Actual current spend is {context.Overview.ActualMonthlyCost:0.##}, budget forecast is {context.Overview.BudgetForecastMonthlyCost:0.##}, and snapshot forecast is {context.Overview.ForecastMonthlyCost:0.##}.",
        };

        if (context.Overview.HasEstimatedFallbackCosts)
        {
            findings.Add($"Estimated fallback totals currently add {context.Overview.EstimatedMonthlyCost:0.##} and should not be treated as actual billed spend.");
        }

        var topCost = observations
            .Where(item => item.MonthlyCost.HasValue && (item.MonthlyCost ?? 0m) > 0m)
            .OrderByDescending(item => item.MonthlyCost)
            .FirstOrDefault();
        if (topCost is not null)
        {
            findings.Add($"{topCost.ResourceName} is the highest-cost resource in scope at {topCost.MonthlyCost:0.##} {topCost.Currency}.");
        }

        var topRecommendation = recommendations
            .OrderByDescending(item => item.PotentialSavings)
            .FirstOrDefault();
        if (topRecommendation is not null)
        {
            findings.Add($"Top savings opportunity in scope is {topRecommendation.ResourceName} with up to {topRecommendation.PotentialSavings:0.##} {topRecommendation.Currency} in potential monthly savings.");
        }

        var highSeverityAnomaly = anomalies
            .FirstOrDefault(item => item.Severity.Equals("High", StringComparison.OrdinalIgnoreCase) || item.Severity.Equals("Critical", StringComparison.OrdinalIgnoreCase));
        if (highSeverityAnomaly is not null)
        {
            findings.Add($"High-severity issue in scope: {highSeverityAnomaly.Title}.");
        }

        return findings.Take(5).ToList();
    }

    private static string DetermineFocusArea(string query)
    {
        if (ContainsAny(query, "storage", "blob", "bucket", "disk", "file share", "filesystem", "volume"))
        {
            return "Storage";
        }

        if (ContainsAny(query, "compute", "vm", "instance", "ec2", "server", "machine"))
        {
            return "Compute";
        }

        if (ContainsAny(query, "database", "sql", "postgres", "mysql", "cosmos", "rds", "db"))
        {
            return "Database";
        }

        if (ContainsAny(query, "network", "vnet", "vpc", "subnet", "gateway", "load balancer", "cdn"))
        {
            return "Networking";
        }

        if (ContainsAny(query, "cost", "spend", "budget", "savings"))
        {
            return "Cost";
        }

        if (ContainsAny(query, "security", "identity", "iam", "access", "policy"))
        {
            return "Security";
        }

        if (ContainsAny(query, "function", "lambda", "container", "kubernetes", "aks", "ecs"))
        {
            return "Runtime";
        }

        return "General";
    }

    private static List<CloudResource> FilterResources(IReadOnlyCollection<CloudResource> resources, string query, string focusArea)
    {
        var tokens = query
            .Split([' ', ',', '.', '?', '!', ':', ';', '/', '\\', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 3)
            .Select(token => token.Trim())
            .Where(token => !StopWords.Contains(token, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var results = resources
            .Where(resource => MatchesFocus(resource, focusArea))
            .Where(resource => tokens.Count == 0 || tokens.Any(token => MatchesToken(resource, token)))
            .OrderBy(resource => resource.Name)
            .ToList();

        if (focusArea == "Cost" && results.Count < 3)
        {
            return resources
                .OrderBy(resource => resource.Name)
                .ToList();
        }

        return results;
    }

    private static bool IsInScope(InsightAnomaly anomaly, string focusArea, IReadOnlyCollection<string> resourceIds, string query)
    {
        if (!string.IsNullOrWhiteSpace(anomaly.ResourceId) && resourceIds.Contains(anomaly.ResourceId, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return focusArea switch
        {
            "Cost" => anomaly.Category.Contains("Cost", StringComparison.OrdinalIgnoreCase) || anomaly.Category.Contains("Budget", StringComparison.OrdinalIgnoreCase),
            "Security" => anomaly.Category.Contains("Security", StringComparison.OrdinalIgnoreCase),
            _ => anomaly.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || anomaly.Summary.Contains(query, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static bool IsInScope(InsightWorkflowSuggestion workflow, string focusArea, IReadOnlyCollection<string> resourceIds, string query)
    {
        if (!string.IsNullOrWhiteSpace(workflow.ResourceId) && resourceIds.Contains(workflow.ResourceId, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return focusArea switch
        {
            "Cost" => workflow.Trigger.Contains("Budget", StringComparison.OrdinalIgnoreCase) || workflow.Trigger.Contains("Recommendation", StringComparison.OrdinalIgnoreCase),
            "Security" => workflow.Trigger.Contains("Security", StringComparison.OrdinalIgnoreCase),
            _ => workflow.Reason.Contains(query, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static bool MatchesFocus(CloudResource resource, string focusArea)
    {
        var type = resource.Type;
        var name = resource.Name;

        return focusArea switch
        {
            "Storage" => ContainsAny(type, "storage", "blob", "bucket", "disk", "file", "volume") ||
                         ContainsAny(name, "storage", "blob", "bucket", "disk", "file"),
            "Compute" => ContainsAny(type, "compute", "virtualmachine", "vm", "instance", "ec2"),
            "Database" => ContainsAny(type, "sql", "database", "postgres", "mysql", "cosmos", "rds"),
            "Networking" => ContainsAny(type, "network", "vnet", "vpc", "subnet", "gateway", "loadbalancer", "cdn"),
            "Security" => ContainsAny(type, "security", "policy", "vault", "keyvault", "firewall", "iam"),
            "Runtime" => ContainsAny(type, "function", "lambda", "container", "kubernetes", "aks", "ecs"),
            _ => true
        };
    }

    private static bool MatchesToken(CloudResource resource, string token) =>
        resource.Name.Contains(token, StringComparison.OrdinalIgnoreCase) ||
        resource.Type.Contains(token, StringComparison.OrdinalIgnoreCase) ||
        resource.Provider.Contains(token, StringComparison.OrdinalIgnoreCase) ||
        resource.Location.Contains(token, StringComparison.OrdinalIgnoreCase) ||
        resource.SubscriptionId.Contains(token, StringComparison.OrdinalIgnoreCase) ||
        resource.ResourceGroupName.Contains(token, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsAny(string input, params string[] values) =>
        values.Any(value => input.Contains(value, StringComparison.OrdinalIgnoreCase));

    private static readonly string[] StopWords =
    [
        "the", "and", "for", "with", "about", "how", "what", "looking", "tell", "into", "from", "your", "their",
        "draco", "cloud", "resource", "resources", "particular", "please", "could", "would", "should", "looking"
    ];
}
