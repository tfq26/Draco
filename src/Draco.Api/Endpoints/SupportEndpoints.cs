using System.Security.Claims;
using System.Text.Json;
using Draco.Application.Models;
using Draco.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Draco.Api.Endpoints;

public static class SupportEndpoints
{
    public static void MapSupportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/support/errors", GetSupportErrorsAsync)
            .RequireAuthorization()
            .WithName("GetSupportErrors");
    }

    private static async Task<IResult> GetSupportErrorsAsync(
        ClaimsPrincipal userPrincipal,
        DracoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var user = await userPrincipal.GetCurrentUserAsync(dbContext, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var recentLogs = await dbContext.WorkflowEvents
            .AsNoTracking()
            .Where(item => item.UserId == user.Id && item.Category == "Support")
            .OrderByDescending(item => item.ReceivedAt)
            .Take(100)
            .Select(item => new
            {
                id = item.Id,
                eventType = item.EventType,
                severity = item.Severity,
                title = item.Title,
                summary = item.Summary,
                occurredAt = item.OccurredAt,
                receivedAt = item.ReceivedAt,
                correlationId = item.CorrelationId,
                processingError = item.ProcessingError,
                metadata = item.RawPayload
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(new
        {
            definitions = SupportErrorCatalog.All,
            recentLogs
        });
    }
}
