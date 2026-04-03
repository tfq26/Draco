using System.Text.Json;
using Draco.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Draco.Api.Endpoints;

public static class AzureMonitorWebhookEndpoints
{
    public static void MapAzureMonitorWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/events/azure/activity-log", HandleAzureActivityLogAsync)
            .WithName("HandleAzureActivityLog");
    }

    private static async Task<IResult> HandleAzureActivityLogAsync(
        HttpRequest request,
        DracoDbContext dbContext,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var secret = configuration["DRACO_EVENT_INGESTION_SECRET"];
        var suppliedCode = request.Query["code"].ToString();

        if (string.IsNullOrWhiteSpace(secret))
        {
            return Results.Problem("Azure monitor ingestion is not configured.");
        }

        if (!string.Equals(secret, suppliedCode, StringComparison.Ordinal))
        {
            return Results.Unauthorized();
        }

        using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var userHintEmail = NormalizeValue(request.Query["userEmail"].ToString())
            ?? NormalizeValue(request.Query["email"].ToString());

        var schemaId = GetString(root, "schemaId");
        if (string.Equals(schemaId, "azureMonitorCommonAlertSchema", StringComparison.OrdinalIgnoreCase))
        {
            return await HandleCommonAlertSchemaAsync(root, userHintEmail, dbContext, cancellationToken);
        }

        return await HandleRawActivityLogPayloadAsync(root, userHintEmail, dbContext, cancellationToken);
    }

    private static async Task<IResult> HandleCommonAlertSchemaAsync(
        JsonElement root,
        string? userHintEmail,
        DracoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var essentials = GetProperty(root, "data", "essentials");
        var alertContext = GetProperty(root, "data", "alertContext");

        var firedDateTime = GetDateTimeOffset(essentials, "firedDateTime") ?? DateTimeOffset.UtcNow;
        var alertId = GetString(essentials, "alertId");
        var alertRule = GetString(essentials, "alertRule");
        var severity = NormalizeAzureSeverity(GetString(essentials, "severity"));
        var signalType = GetString(essentials, "signalType");
        var monitorCondition = GetString(essentials, "monitorCondition");
        var description = GetString(essentials, "description");
        var configurationItems = GetStringArray(essentials, "configurationItems");
        var resourceId = configurationItems.FirstOrDefault() ?? GetString(alertContext, "resourceId");
        var subscriptionId = ExtractSubscriptionId(resourceId)
            ?? GetString(alertContext, "subscriptionId")
            ?? string.Empty;
        var email = ResolveAzureUserEmail(root, essentials, alertContext);

        var user = await ResolveAzureUserAsync(userHintEmail ?? email, subscriptionId, resourceId, dbContext, cancellationToken);
        if (user is null)
        {
            return Results.BadRequest(new
            {
                message = "Unable to resolve a Draco user for the Azure alert.",
                userHintEmail,
                email,
                subscriptionId,
                resourceId
            });
        }

        var title = !string.IsNullOrWhiteSpace(alertRule)
            ? alertRule
            : !string.IsNullOrWhiteSpace(signalType)
                ? $"Azure {signalType} alert"
                : "Azure activity alert";

        var summary = description
            ?? BuildSummary(signalType, monitorCondition, resourceId, alertContext);

        var workflowEvent = new Draco.Domain.Entities.WorkflowEvent
        {
            UserId = user.Id,
            Source = "Azure Monitor",
            EventType = "AzureActivityLogAlert",
            Category = "AzureActivityLog",
            Severity = severity,
            Provider = "Azure",
            SubscriptionId = subscriptionId,
            ResourceId = resourceId,
            Title = title,
            Summary = summary,
            Status = "Pending",
            OccurredAt = firedDateTime,
            ReceivedAt = DateTimeOffset.UtcNow,
            CorrelationId = alertId,
            RawPayload = root.GetRawText()
        };

        dbContext.WorkflowEvents.Add(workflowEvent);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Accepted($"/api/events/{workflowEvent.Id}", new
        {
            eventId = workflowEvent.Id,
            status = workflowEvent.Status,
            message = "Azure activity log alert accepted for processing."
        });
    }

    private static async Task<IResult> HandleRawActivityLogPayloadAsync(
        JsonElement root,
        string? userHintEmail,
        DracoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var email = GetString(root, "email");
        var eventType = GetString(root, "eventType") ?? "AzureActivityEvent";
        var title = GetString(root, "title") ?? eventType;
        var summary = GetString(root, "summary") ?? title;
        var resourceId = GetString(root, "resourceId");
        var subscriptionId = GetString(root, "subscriptionId") ?? ExtractSubscriptionId(resourceId) ?? string.Empty;
        var severity = NormalizeAzureSeverity(GetString(root, "severity"));

        var user = await ResolveAzureUserAsync(userHintEmail ?? email, subscriptionId, resourceId, dbContext, cancellationToken);
        if (user is null)
        {
            return Results.BadRequest(new
            {
                message = "Unable to resolve a Draco user for the Azure payload.",
                userHintEmail,
                email,
                subscriptionId,
                resourceId
            });
        }

        var workflowEvent = new Draco.Domain.Entities.WorkflowEvent
        {
            UserId = user.Id,
            Source = GetString(root, "source") ?? "Azure Monitor",
            EventType = eventType,
            Category = GetString(root, "category") ?? "AzureActivityLog",
            Severity = severity,
            Provider = "Azure",
            SubscriptionId = subscriptionId,
            ResourceId = resourceId,
            Title = title,
            Summary = summary,
            Status = "Pending",
            OccurredAt = GetDateTimeOffset(root, "occurredAt") ?? DateTimeOffset.UtcNow,
            ReceivedAt = DateTimeOffset.UtcNow,
            CorrelationId = GetString(root, "correlationId"),
            RawPayload = root.GetRawText()
        };

        dbContext.WorkflowEvents.Add(workflowEvent);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Accepted($"/api/events/{workflowEvent.Id}", new
        {
            eventId = workflowEvent.Id,
            status = workflowEvent.Status,
            message = "Azure payload accepted for processing."
        });
    }

    private static async Task<Draco.Domain.Entities.UserAccount?> ResolveUserByEmailAsync(
        string? email,
        DracoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        return await dbContext.UserAccounts.FirstOrDefaultAsync(
            user => user.Email == normalizedEmail,
            cancellationToken);
    }

    private static async Task<Draco.Domain.Entities.UserAccount?> ResolveAzureUserAsync(
        string? email,
        string? subscriptionId,
        string? resourceId,
        DracoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var resolvedByEmail = await ResolveUserByEmailAsync(email, dbContext, cancellationToken);
        if (resolvedByEmail is not null)
        {
            return resolvedByEmail;
        }

        var normalizedSubscriptionId = NormalizeValue(subscriptionId) ?? ExtractSubscriptionId(resourceId);
        if (string.IsNullOrWhiteSpace(normalizedSubscriptionId))
        {
            return null;
        }

        var matchingUsers = await dbContext.CloudConnections
            .AsNoTracking()
            .Include(connection => connection.User)
            .Where(connection =>
                connection.IsActive &&
                connection.Provider == "Azure" &&
                connection.SubscriptionId == normalizedSubscriptionId &&
                connection.User != null)
            .Select(connection => connection.User!)
            .Distinct()
            .ToListAsync(cancellationToken);

        return matchingUsers.Count == 1
            ? matchingUsers[0]
            : null;
    }

    private static string? ResolveAzureUserEmail(JsonElement root, JsonElement essentials, JsonElement alertContext)
    {
        return GetString(root, "email")
            ?? GetString(essentials, "email")
            ?? GetString(alertContext, "email")
            ?? GetString(alertContext, "caller")
            ?? GetString(alertContext, "claims", "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/upn")
            ?? GetString(alertContext, "claims", "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress");
    }

    private static string BuildSummary(
        string? signalType,
        string? monitorCondition,
        string? resourceId,
        JsonElement alertContext)
    {
        var operationName = GetString(alertContext, "operationName");
        var status = GetString(alertContext, "status");

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(signalType))
        {
            parts.Add(signalType);
        }
        if (!string.IsNullOrWhiteSpace(monitorCondition))
        {
            parts.Add(monitorCondition);
        }
        if (!string.IsNullOrWhiteSpace(operationName))
        {
            parts.Add(operationName);
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            parts.Add(status);
        }
        if (!string.IsNullOrWhiteSpace(resourceId))
        {
            parts.Add(resourceId);
        }

        return parts.Count == 0
            ? "Azure activity log alert received."
            : string.Join(" | ", parts);
    }

    private static string NormalizeAzureSeverity(string? severity) =>
        severity?.Trim().ToLowerInvariant() switch
        {
            "sev0" => "Critical",
            "sev1" => "High",
            "sev2" => "Medium",
            "sev3" => "Medium",
            "sev4" => "Low",
            _ => string.IsNullOrWhiteSpace(severity) ? "Medium" : severity.Trim()
        };

    private static string? ExtractSubscriptionId(string? resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return null;
        }

        var segments = resourceId.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (segments[index].Equals("subscriptions", StringComparison.OrdinalIgnoreCase))
            {
                return segments[index + 1];
            }
        }

        return null;
    }

    private static string? NormalizeValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static JsonElement GetProperty(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return default;
            }
        }

        return current;
    }

    private static string? GetString(JsonElement element, params string[] path)
    {
        var current = path.Length == 0 ? element : GetProperty(element, path);
        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    private static DateTimeOffset? GetDateTimeOffset(JsonElement element, params string[] path)
    {
        var value = GetString(element, path);
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement element, params string[] path)
    {
        var current = path.Length == 0 ? element : GetProperty(element, path);
        if (current.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return current.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .ToList();
    }
}
