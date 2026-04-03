using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Draco.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Draco.Api.Endpoints;

public static class EventWorkflowEndpoints
{
    public static void MapEventWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/events/ingest", IngestEventAsync)
            .WithName("IngestWorkflowEvent");

        var eventsGroup = app.MapGroup("/api/events").RequireAuthorization();
        eventsGroup.MapGet("/", ListEventsAsync)
            .WithName("ListWorkflowEvents");

        var workflowGroup = app.MapGroup("/api/workflows").RequireAuthorization();
        workflowGroup.MapGet("/runs", ListWorkflowRunsAsync)
            .WithName("ListWorkflowRuns");
        workflowGroup.MapPatch("/runs/{id:guid}", UpdateWorkflowRunStatusAsync)
            .WithName("UpdateWorkflowRunStatus");
    }

    private static async Task<IResult> IngestEventAsync(
        HttpContext httpContext,
        DracoDbContext dbContext,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var secret = configuration["DRACO_EVENT_INGESTION_SECRET"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            return Results.Problem("Event ingestion is not configured.");
        }

        using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);

        if (!TryValidateSignature(httpContext.Request, rawBody, secret, out var validationError))
        {
            return Results.Unauthorized();
        }

        var request = JsonSerializer.Deserialize<WorkflowEventIngestionRequest>(rawBody, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (request is null || string.IsNullOrWhiteSpace(request.EventType) || string.IsNullOrWhiteSpace(request.Title))
        {
            return Results.BadRequest(new { message = "Invalid event payload." });
        }

        var user = await ResolveEventUserAsync(request, dbContext, cancellationToken);
        if (user is null)
        {
            return Results.BadRequest(new { message = "Unable to resolve a Draco user for the event." });
        }

        var workflowEvent = new Draco.Domain.Entities.WorkflowEvent
        {
            UserId = user.Id,
            Source = string.IsNullOrWhiteSpace(request.Source) ? "Webhook" : request.Source.Trim(),
            EventType = request.EventType.Trim(),
            Category = string.IsNullOrWhiteSpace(request.Category) ? request.EventType.Trim() : request.Category.Trim(),
            Severity = string.IsNullOrWhiteSpace(request.Severity) ? "Medium" : request.Severity.Trim(),
            Provider = string.IsNullOrWhiteSpace(request.Provider) ? string.Empty : AuthEndpoints.NormalizeProvider(request.Provider),
            SubscriptionId = request.SubscriptionId?.Trim() ?? string.Empty,
            ResourceId = request.ResourceId?.Trim(),
            Title = request.Title.Trim(),
            Summary = request.Summary?.Trim() ?? request.Title.Trim(),
            Status = "Pending",
            OccurredAt = request.OccurredAt ?? DateTimeOffset.UtcNow,
            ReceivedAt = DateTimeOffset.UtcNow,
            CorrelationId = request.CorrelationId,
            RawPayload = rawBody
        };

        dbContext.WorkflowEvents.Add(workflowEvent);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Accepted($"/api/events/{workflowEvent.Id}", new
        {
            eventId = workflowEvent.Id,
            status = workflowEvent.Status,
            message = "Event accepted for processing."
        });
    }

    private static async Task<IResult> ListEventsAsync(
        ClaimsPrincipal userPrincipal,
        DracoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var user = await userPrincipal.GetCurrentUserAsync(dbContext, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var events = await dbContext.WorkflowEvents
            .AsNoTracking()
            .Where(item => item.UserId == user.Id)
            .OrderByDescending(item => item.ReceivedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        return Results.Ok(events);
    }

    private static async Task<IResult> ListWorkflowRunsAsync(
        ClaimsPrincipal userPrincipal,
        DracoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var user = await userPrincipal.GetCurrentUserAsync(dbContext, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var runs = await dbContext.WorkflowRuns
            .AsNoTracking()
            .Where(item => item.UserId == user.Id)
            .OrderByDescending(item => item.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        return Results.Ok(runs);
    }

    private static async Task<IResult> UpdateWorkflowRunStatusAsync(
        Guid id,
        [FromBody] UpdateWorkflowRunStatusRequest request,
        ClaimsPrincipal userPrincipal,
        DracoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var user = await userPrincipal.GetCurrentUserAsync(dbContext, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var run = await dbContext.WorkflowRuns
            .FirstOrDefaultAsync(item => item.Id == id && item.UserId == user.Id, cancellationToken);

        if (run is null)
        {
            return Results.NotFound(new { message = "Workflow run not found." });
        }

        run.Status = string.IsNullOrWhiteSpace(request.Status) ? run.Status : request.Status.Trim();
        run.UpdatedAt = DateTimeOffset.UtcNow;
        if (run.Status is "Completed" or "Dismissed" or "Failed")
        {
            run.CompletedAt = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(run);
    }

    private static async Task<Draco.Domain.Entities.UserAccount?> ResolveEventUserAsync(
        WorkflowEventIngestionRequest request,
        DracoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (request.UserId.HasValue)
        {
            return await dbContext.UserAccounts
                .FirstOrDefaultAsync(user => user.Id == request.UserId.Value, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(request.AuthId))
        {
            return await dbContext.UserAccounts
                .FirstOrDefaultAsync(user => user.AuthId == request.AuthId, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var normalizedEmail = request.Email.Trim().ToLowerInvariant();
            var byEmail = await dbContext.UserAccounts
                .FirstOrDefaultAsync(user => user.Email == normalizedEmail, cancellationToken);
            if (byEmail is not null)
            {
                return byEmail;
            }
        }

        var normalizedProvider = string.IsNullOrWhiteSpace(request.Provider)
            ? null
            : AuthEndpoints.NormalizeProvider(request.Provider);
        var normalizedSubscriptionId = string.IsNullOrWhiteSpace(request.SubscriptionId)
            ? null
            : request.SubscriptionId.Trim();

        if (!string.IsNullOrWhiteSpace(normalizedProvider) && !string.IsNullOrWhiteSpace(normalizedSubscriptionId))
        {
            var matchingUsers = await dbContext.CloudConnections
                .AsNoTracking()
                .Include(connection => connection.User)
                .Where(connection =>
                    connection.IsActive &&
                    connection.Provider == normalizedProvider &&
                    connection.SubscriptionId == normalizedSubscriptionId &&
                    connection.User != null)
                .Select(connection => connection.User!)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (matchingUsers.Count == 1)
            {
                return matchingUsers[0];
            }
        }

        return null;
    }

    private static bool TryValidateSignature(HttpRequest request, string rawBody, string secret, out string? error)
    {
        error = null;

        var signature = request.Headers["x-draco-signature"].ToString();
        var timestamp = request.Headers["x-draco-timestamp"].ToString();

        if (string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(timestamp))
        {
            error = "Missing signature headers.";
            return false;
        }

        if (!long.TryParse(timestamp, out var unixSeconds))
        {
            error = "Invalid timestamp.";
            return false;
        }

        var requestTime = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        if (DateTimeOffset.UtcNow - requestTime > TimeSpan.FromMinutes(5))
        {
            error = "Signature expired.";
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var payload = $"{timestamp}.{rawBody}";
        var computed = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        var normalizedSignature = signature.Trim().ToLowerInvariant();

        var matches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed),
            Encoding.UTF8.GetBytes(normalizedSignature));

        if (!matches)
        {
            error = "Signature mismatch.";
        }

        return matches;
    }
}

public sealed record WorkflowEventIngestionRequest(
    Guid? UserId,
    string? AuthId,
    string? Email,
    string? Source,
    string EventType,
    string? Category,
    string? Severity,
    string? Provider,
    string? SubscriptionId,
    string? ResourceId,
    string Title,
    string? Summary,
    string? CorrelationId,
    DateTimeOffset? OccurredAt,
    object? Payload);

public sealed record UpdateWorkflowRunStatusRequest(string Status);
