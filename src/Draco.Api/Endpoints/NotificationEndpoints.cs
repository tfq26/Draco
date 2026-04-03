using System.Security.Claims;
using Draco.Application.Interfaces;
using Draco.Domain.Entities;
using Draco.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Draco.Api.Endpoints;

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications").RequireAuthorization();

        group.MapGet("/", GetNotificationsAsync)
            .WithName("GetNotifications");

        group.MapPatch("/{id}/read", MarkAsReadAsync)
            .WithName("MarkAsRead");

        group.MapPost("/clear-all", ClearAllNotificationsAsync)
            .WithName("ClearAllNotifications");
            
        group.MapPost("/test", CreateTestNotificationAsync)
            .WithName("CreateTestNotification");
    }

    private static async Task<IResult> GetNotificationsAsync(
        DracoDbContext dbContext,
        ClaimsPrincipal userPrincipal,
        CancellationToken cancellationToken)
    {
        var userAccount = await userPrincipal.GetCurrentUserAsync(dbContext, cancellationToken);
        if (userAccount is null) return Results.Unauthorized();

        var notifications = await dbContext.SystemNotifications
            .AsNoTracking()
            .Where(n => n.UserId == userAccount.Id)
            .Where(n => n.ResolvedAt == null)
            .OrderByDescending(n => n.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        return Results.Ok(notifications.Select(ToNotificationDto));
    }

    private static async Task<IResult> MarkAsReadAsync(
        int id,
        DracoDbContext dbContext,
        ClaimsPrincipal userPrincipal,
        CancellationToken cancellationToken)
    {
        var userAccount = await userPrincipal.GetCurrentUserAsync(dbContext, cancellationToken);
        if (userAccount is null) return Results.Unauthorized();

        var notification = await dbContext.SystemNotifications
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userAccount.Id, cancellationToken);

        if (notification == null) return Results.NotFound();

        notification.IsRead = true;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok();
    }

    private static async Task<IResult> ClearAllNotificationsAsync(
        DracoDbContext dbContext,
        ClaimsPrincipal userPrincipal,
        CancellationToken cancellationToken)
    {
        var userAccount = await userPrincipal.GetCurrentUserAsync(dbContext, cancellationToken);
        if (userAccount is null) return Results.Unauthorized();

        var notifications = await dbContext.SystemNotifications
            .Where(n => n.UserId == userAccount.Id)
            .ToListAsync(cancellationToken);

        dbContext.SystemNotifications.RemoveRange(notifications);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok();
    }

    private static async Task<IResult> CreateTestNotificationAsync(
        DracoDbContext dbContext,
        IMessagingService messagingService,
        ClaimsPrincipal userPrincipal,
        CancellationToken cancellationToken)
    {
        var userAccount = await userPrincipal.GetCurrentUserAsync(dbContext, cancellationToken);
        if (userAccount is null) return Results.Unauthorized();

        var notification = new SystemNotification
        {
            UserId = userAccount.Id,
            NotificationKey = $"manual:test:{Guid.NewGuid():N}",
            Title = "Test Notification",
            Message = "This is a test notification generated at " + FormatUserLocalTimestamp(DateTimeOffset.UtcNow, userAccount.TimeZoneId),
            Type = "Info",
            Severity = "Info",
            CreatedAt = DateTime.UtcNow,
            LastEvaluatedAt = DateTime.UtcNow,
            Category = "System",
            SourceRule = "manual-test"
        };

        dbContext.SystemNotifications.Add(notification);
        await dbContext.SaveChangesAsync(cancellationToken);

        var channels = ParsePreferredChannels(userAccount.PreferredChannel);
        var deliveryAttempts = new List<string>();
        var deliverySucceeded = false;
        string deliveryMessage;
        var smsRecipients = ParseRecipients(userAccount.SmsRecipientsJson, userAccount.Phone);
        var whatsAppRecipients = ParseRecipients(userAccount.WhatsAppRecipientsJson);

        if (smsRecipients.Count == 0 && whatsAppRecipients.Count == 0)
        {
            deliveryMessage = "Test notification saved, but no delivery recipients are configured on this account.";
        }
        else if (channels.Count == 0)
        {
            deliveryMessage = "Test notification saved, but the preferred channel is not configured for mobile delivery.";
        }
        else
        {
            if (channels.Contains("SMS"))
            {
                foreach (var recipient in smsRecipients)
                {
                    deliverySucceeded |= await messagingService.SendMessageAsync(recipient, notification.Message, cancellationToken);
                }

                if (smsRecipients.Count > 0)
                {
                    deliveryAttempts.Add($"SMS ({smsRecipients.Count})");
                }
            }

            if (channels.Contains("WhatsApp"))
            {
                foreach (var recipient in whatsAppRecipients)
                {
                    deliverySucceeded |= await messagingService.SendWhatsAppMessageAsync(recipient, notification.Message, cancellationToken);
                }

                if (whatsAppRecipients.Count > 0)
                {
                    deliveryAttempts.Add($"WhatsApp ({whatsAppRecipients.Count})");
                }
            }

            if (deliveryAttempts.Count == 0)
            {
                deliveryMessage = "Test notification saved, but the preferred channel is not configured for mobile delivery.";
            }
            else if (deliverySucceeded)
            {
                deliveryMessage = deliveryAttempts.Count switch
                {
                    1 => $"Test notification saved and delivered through {deliveryAttempts[0]}.",
                    _ => $"Test notification saved and delivered through {string.Join(" and ", deliveryAttempts)}."
                };
            }
            else
            {
                deliveryMessage = deliveryAttempts.Count switch
                {
                    1 => $"Test notification saved, but {deliveryAttempts[0]} delivery failed. Check Twilio configuration on the deployed API.",
                    _ => $"Test notification saved, but delivery failed through {string.Join(" and ", deliveryAttempts)}. Check Twilio configuration on the deployed API."
                };
            }
        }

        if (deliverySucceeded)
        {
            notification.LastDeliveredAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Results.Ok(new
        {
            notification = ToNotificationDto(notification),
            deliveryAttempted = deliveryAttempts.Count > 0,
            deliverySucceeded,
            channels = deliveryAttempts,
            message = deliveryMessage
        });
    }

    private static HashSet<string> ParsePreferredChannels(string? rawChannels)
    {
        var channels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(rawChannels))
        {
            return channels;
        }

        foreach (var channel in rawChannels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(channel, "Messages", StringComparison.OrdinalIgnoreCase))
            {
                channels.Add("SMS");
                continue;
            }

            if (string.Equals(channel, "SMS", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(channel, "WhatsApp", StringComparison.OrdinalIgnoreCase))
            {
                channels.Add(channel);
            }
        }

        return channels;
    }

    private static List<string> ParseRecipients(string? recipientsJson, string? fallbackRecipient = null)
    {
        try
        {
            var parsedRecipients = string.IsNullOrWhiteSpace(recipientsJson)
                ? Array.Empty<string>()
                : JsonSerializer.Deserialize<string[]>(recipientsJson) ?? Array.Empty<string>();

            var normalizedRecipients = parsedRecipients
                .Select(recipient => recipient.Trim())
                .Where(recipient => !string.IsNullOrWhiteSpace(recipient))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (normalizedRecipients.Count == 0 && !string.IsNullOrWhiteSpace(fallbackRecipient))
            {
                normalizedRecipients.Add(fallbackRecipient.Trim());
            }

            return normalizedRecipients;
        }
        catch
        {
            return string.IsNullOrWhiteSpace(fallbackRecipient)
                ? new List<string>()
                : new List<string> { fallbackRecipient.Trim() };
        }
    }

    private static object ToNotificationDto(SystemNotification notification) => new
    {
        id = notification.Id,
        notification.NotificationKey,
        notification.Title,
        notification.Message,
        notification.Type,
        notification.Severity,
        notification.CreatedAt,
        notification.LastEvaluatedAt,
        notification.LastDeliveredAt,
        notification.ResolvedAt,
        notification.IsRead,
        notification.ResourceUrl,
        notification.Category,
        notification.Provider,
        notification.SubscriptionId,
        notification.ResourceId,
        notification.Service,
        notification.SourceRule,
        notification.Metadata
    };

    private static string FormatUserLocalTimestamp(DateTimeOffset timestamp, string? timeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId))
        {
            try
            {
                var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                var localTime = TimeZoneInfo.ConvertTime(timestamp, timeZone);
                return $"{localTime:dddd, MMMM d, yyyy h:mm tt} {timeZone.Id}";
            }
            catch
            {
            }
        }

        return $"{timestamp:dddd, MMMM d, yyyy h:mm tt} UTC";
    }
}
