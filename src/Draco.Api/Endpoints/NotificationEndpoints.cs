using System.Security.Claims;
using Draco.Domain.Entities;
using Draco.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

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
            Message = "This is a test notification generated at " + DateTime.UtcNow.ToString("f"),
            Type = "Info",
            Severity = "Info",
            CreatedAt = DateTime.UtcNow,
            LastEvaluatedAt = DateTime.UtcNow,
            Category = "System",
            SourceRule = "manual-test"
        };

        dbContext.SystemNotifications.Add(notification);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToNotificationDto(notification));
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
}
