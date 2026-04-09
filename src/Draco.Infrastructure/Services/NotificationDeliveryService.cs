using Draco.Application.Interfaces;
using Draco.Application.Models;
using Draco.Domain.Entities;
using Draco.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Draco.Infrastructure.Services;

public class NotificationDeliveryService : INotificationDeliveryService
{
    private readonly DracoDbContext _dbContext;
    private readonly IMessagingService _messagingService;
    private readonly IEmailService _emailService;
    private readonly ILogger<NotificationDeliveryService> _logger;

    public NotificationDeliveryService(
        DracoDbContext dbContext,
        IMessagingService messagingService,
        IEmailService emailService,
        ILogger<NotificationDeliveryService> logger)
    {
        _dbContext = dbContext;
        _messagingService = messagingService;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<bool> DeliverAsync(UserAccount user, SystemNotification notification, CancellationToken cancellationToken = default)
    {
        var preferences = NotificationDeliveryPreferencesSerializer.Resolve(user);
        var delivered = false;

        if (preferences.MessagesEnabled)
        {
            if (preferences.MessagesNumbers.Count == 0)
            {
                _logger.LogWarning(
                    "Messages delivery skipped for user {UserId} because no destination numbers are configured.",
                    user.Id);
            }
            else
            {
                var messageBody = await BuildMessagesBodyAsync(user.Id, notification, cancellationToken);
                foreach (var recipient in preferences.MessagesNumbers)
                {
                    delivered |= await _messagingService.SendMessageAsync(
                        recipient,
                        messageBody,
                        cancellationToken);
                }
            }
        }

        if (preferences.EmailEnabled)
        {
            if (string.IsNullOrWhiteSpace(preferences.EmailAddress))
            {
                _logger.LogWarning(
                    "Email delivery skipped for user {UserId} because no destination email is configured.",
                    user.Id);
            }
            else
            {
                await _emailService.SendEmailAsync(
                    preferences.EmailAddress,
                    BuildEmailSubject(notification),
                    BuildEmailBody(notification),
                    cancellationToken);
                delivered = true;
            }
        }

        if (preferences.WhatsAppEnabled)
        {
            if (preferences.WhatsAppNumbers.Count == 0)
            {
                _logger.LogWarning(
                    "WhatsApp delivery skipped for user {UserId} because no destination numbers are configured.",
                    user.Id);
            }
            else
            {
                var messageBody = await BuildMessagesBodyAsync(user.Id, notification, cancellationToken);
                foreach (var recipient in preferences.WhatsAppNumbers)
                {
                    delivered |= await _messagingService.SendWhatsAppMessageAsync(
                        recipient,
                        messageBody,
                        cancellationToken);
                }
            }
        }

        return delivered;
    }

    private static string BuildEmailSubject(SystemNotification notification) =>
        $"Draco {notification.Severity} Alert: {notification.Title}";

    private static string BuildEmailBody(SystemNotification notification)
    {
        var resourceLine = string.IsNullOrWhiteSpace(notification.ResourceId)
            ? string.Empty
            : $"<p><strong>Resource:</strong> {notification.ResourceId}</p>";
        var providerLine = string.IsNullOrWhiteSpace(notification.Provider)
            ? string.Empty
            : $"<p><strong>Provider:</strong> {notification.Provider}</p>";

        return $"""
            <h2>{notification.Title}</h2>
            <p>{notification.Message}</p>
            <p><strong>Severity:</strong> {notification.Severity}</p>
            <p><strong>Category:</strong> {notification.Category ?? "System"}</p>
            {providerLine}
            {resourceLine}
            """;
    }

    private async Task<string> BuildMessagesBodyAsync(
        Guid userId,
        SystemNotification notification,
        CancellationToken cancellationToken)
    {
        var providerSegment = string.IsNullOrWhiteSpace(notification.Provider)
            ? string.Empty
            : $" [{notification.Provider}]";
        var workflowReference = await ResolveWorkflowReferenceAsync(userId, notification, cancellationToken);
        var lines = new List<string>
        {
            $"Draco {notification.Severity}{providerSegment}",
            notification.Title,
            notification.Message
        };

        if (workflowReference is not null)
        {
            lines.Add($"Open workflow: {workflowReference.Value.ShortId}.");
            lines.Add($"Reply APPROVE {workflowReference.Value.ShortId}, DISMISS {workflowReference.Value.ShortId}, or STATUS.");
        }
        else
        {
            lines.Add("Reply STATUS to review current workflow items.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private async Task<WorkflowReference?> ResolveWorkflowReferenceAsync(
        Guid userId,
        SystemNotification notification,
        CancellationToken cancellationToken)
    {
        var openRunsQuery = _dbContext.WorkflowRuns
            .AsNoTracking()
            .Where(run => run.UserId == userId && run.Status == "Open");

        Draco.Domain.Entities.WorkflowRun? matchingRun = null;

        if (!string.IsNullOrWhiteSpace(notification.ResourceId))
        {
            matchingRun = await openRunsQuery
                .Where(run => run.ResourceId == notification.ResourceId)
                .OrderByDescending(run => run.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (matchingRun is null && !string.IsNullOrWhiteSpace(notification.SubscriptionId))
        {
            matchingRun = await openRunsQuery
                .Where(run => run.SubscriptionId == notification.SubscriptionId)
                .OrderByDescending(run => run.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (matchingRun is null && !string.IsNullOrWhiteSpace(notification.Category))
        {
            matchingRun = await openRunsQuery
                .Where(run => run.Trigger == notification.Category)
                .OrderByDescending(run => run.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (matchingRun is null)
        {
            matchingRun = await openRunsQuery
                .OrderByDescending(run => run.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return matchingRun is null
            ? null
            : new WorkflowReference(matchingRun.Id, ShortId(matchingRun.Id));
    }

    private static string ShortId(Guid id) =>
        id.ToString("N")[..8].ToUpperInvariant();

    private readonly record struct WorkflowReference(Guid Id, string ShortId);
}
