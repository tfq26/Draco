using Draco.Application.Interfaces;
using Draco.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Draco.Infrastructure.Services;

public class WorkflowEventService
{
    private readonly DracoDbContext _dbContext;
    private readonly IInsightContextService _insightContextService;
    private readonly ILogger<WorkflowEventService> _logger;

    public WorkflowEventService(
        DracoDbContext dbContext,
        IInsightContextService insightContextService,
        ILogger<WorkflowEventService> logger)
    {
        _dbContext = dbContext;
        _insightContextService = insightContextService;
        _logger = logger;
    }

    public async Task ProcessPendingEventAsync(Guid workflowEventId, CancellationToken cancellationToken = default)
    {
        var workflowEvent = await _dbContext.WorkflowEvents
            .FirstOrDefaultAsync(item => item.Id == workflowEventId, cancellationToken);

        if (workflowEvent is null || workflowEvent.Status != "Pending")
        {
            return;
        }

        workflowEvent.Status = "Processing";
        workflowEvent.AttemptCount += 1;
        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var context = await _insightContextService.BuildForUserAsync(workflowEvent.UserId, cancellationToken);
            if (context is null)
            {
                workflowEvent.Status = "Failed";
                workflowEvent.ProcessingError = "Unable to build prepared insight context for the event user.";
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            var matchingSuggestions = context.WorkflowSuggestions
                .Where(item => MatchesEvent(item, workflowEvent))
                .Take(5)
                .ToList();

            if (matchingSuggestions.Count == 0)
            {
                matchingSuggestions.Add(new Draco.Application.Models.InsightWorkflowSuggestion
                {
                    Id = $"workflow:event:{workflowEvent.Id}",
                    Trigger = workflowEvent.Category,
                    SuggestedAction = DetermineFallbackAction(workflowEvent),
                    Severity = workflowEvent.Severity,
                    Reason = workflowEvent.Summary,
                    Provider = workflowEvent.Provider,
                    SubscriptionId = workflowEvent.SubscriptionId,
                    ResourceId = workflowEvent.ResourceId,
                    CanAutoRun = workflowEvent.Category is "ConnectionHealth" or "Budget"
                });
            }

            foreach (var suggestion in matchingSuggestions)
            {
                var exists = await _dbContext.WorkflowRuns.AnyAsync(
                    run => run.WorkflowEventId == workflowEvent.Id &&
                           run.SuggestedAction == suggestion.SuggestedAction &&
                           run.Trigger == suggestion.Trigger,
                    cancellationToken);

                if (exists)
                {
                    continue;
                }

                _dbContext.WorkflowRuns.Add(new Draco.Domain.Entities.WorkflowRun
                {
                    UserId = workflowEvent.UserId,
                    WorkflowEventId = workflowEvent.Id,
                    WorkflowType = "EventReaction",
                    Trigger = suggestion.Trigger,
                    SuggestedAction = suggestion.SuggestedAction,
                    Severity = suggestion.Severity,
                    Provider = suggestion.Provider,
                    SubscriptionId = suggestion.SubscriptionId ?? workflowEvent.SubscriptionId,
                    ResourceId = suggestion.ResourceId ?? workflowEvent.ResourceId,
                    Status = "Open",
                    CanAutoRun = suggestion.CanAutoRun,
                    Reason = suggestion.Reason,
                    Recommendation = context.Recommendations
                        .FirstOrDefault(rec =>
                            (!string.IsNullOrWhiteSpace(workflowEvent.ResourceId) && rec.ResourceId == workflowEvent.ResourceId) ||
                            (!string.IsNullOrWhiteSpace(workflowEvent.SubscriptionId) && rec.SubscriptionId == workflowEvent.SubscriptionId))
                        ?.Description,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }

            workflowEvent.Status = "Processed";
            workflowEvent.ProcessedAt = DateTimeOffset.UtcNow;
            workflowEvent.ProcessingError = null;

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed processing workflow event {WorkflowEventId}", workflowEventId);
            workflowEvent.Status = workflowEvent.AttemptCount >= 5 ? "Failed" : "Pending";
            workflowEvent.ProcessingError = ex.Message;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static bool MatchesEvent(Draco.Application.Models.InsightWorkflowSuggestion suggestion, Draco.Domain.Entities.WorkflowEvent workflowEvent)
    {
        if (!string.IsNullOrWhiteSpace(suggestion.ResourceId) &&
            !string.IsNullOrWhiteSpace(workflowEvent.ResourceId) &&
            string.Equals(suggestion.ResourceId, workflowEvent.ResourceId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(suggestion.SubscriptionId) &&
            !string.IsNullOrWhiteSpace(workflowEvent.SubscriptionId) &&
            string.Equals(suggestion.SubscriptionId, workflowEvent.SubscriptionId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(suggestion.Trigger, workflowEvent.Category, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(suggestion.Trigger, workflowEvent.EventType, StringComparison.OrdinalIgnoreCase);
    }

    private static string DetermineFallbackAction(Draco.Domain.Entities.WorkflowEvent workflowEvent) =>
        workflowEvent.Category switch
        {
            "ConnectionHealth" => "queue-connection-resync",
            "Budget" => "trigger-budget-alert",
            "Security" => "open-security-review",
            "Deployment" => "open-deployment-investigation",
            _ => "review-event"
        };
}
