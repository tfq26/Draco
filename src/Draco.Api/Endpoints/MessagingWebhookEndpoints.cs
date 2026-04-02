using System.Security.Cryptography;
using System.Text;
using Draco.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Draco.Api.Endpoints;

public static class MessagingWebhookEndpoints
{
    public static void MapMessagingWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/webhooks/twilio/messages", HandleIncomingTwilioMessageAsync)
            .WithName("HandleIncomingTwilioMessage");
    }

    private static async Task<IResult> HandleIncomingTwilioMessageAsync(
        HttpRequest request,
        DracoDbContext dbContext,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var authToken = configuration["Twilio:AuthToken"] ?? configuration["TWILIO_AUTH_TOKEN"];
        if (string.IsNullOrWhiteSpace(authToken))
        {
            return Results.Problem("Twilio inbound webhook is not configured.", statusCode: StatusCodes.Status500InternalServerError);
        }

        if (!request.HasFormContentType)
        {
            return Results.BadRequest("Twilio webhook requires form content.");
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var signature = request.Headers["X-Twilio-Signature"].ToString();
        var requestUrl = BuildExternalRequestUrl(request);

        if (!TwilioRequestValidator.IsValid(requestUrl, form, signature, authToken))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var from = NormalizePhoneNumber(form["From"].ToString());
        var to = NormalizePhoneNumber(form["To"].ToString());
        var body = form["Body"].ToString().Trim();
        var messageSid = form["MessageSid"].ToString();

        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(body))
        {
            return TwiML("Thanks, Draco received your message but it was empty.");
        }

        var user = await dbContext.UserAccounts
            .FirstOrDefaultAsync(account => account.Phone == from, cancellationToken);

        if (user is null)
        {
            return TwiML("We could not match this number to a Draco user yet.");
        }

        var commandResult = await MessagingCommandProcessor.ProcessAsync(user.Id, body, dbContext, cancellationToken);

        dbContext.WorkflowEvents.Add(new Draco.Domain.Entities.WorkflowEvent
        {
            UserId = user.Id,
            Source = "Twilio",
            EventType = "IncomingMessage",
            Category = "Messaging",
            Severity = "Info",
            Provider = "Twilio",
            SubscriptionId = string.Empty,
            ResourceId = null,
            Title = "Incoming user message",
            Summary = body,
            Status = "Pending",
            OccurredAt = DateTimeOffset.UtcNow,
            ReceivedAt = DateTimeOffset.UtcNow,
            CorrelationId = string.IsNullOrWhiteSpace(messageSid) ? null : messageSid,
            RawPayload = BuildRawPayloadSnapshot(form)
        });

        dbContext.SystemNotifications.Add(new Draco.Domain.Entities.SystemNotification
        {
            UserId = user.Id,
            NotificationKey = $"twilio:incoming:{messageSid}",
            Title = "New message received",
            Message = $"From {from}: {body}{(string.IsNullOrWhiteSpace(commandResult.Summary) ? string.Empty : $" | {commandResult.Summary}")}",
            Type = commandResult.NotificationType,
            Severity = commandResult.NotificationSeverity,
            CreatedAt = DateTime.UtcNow,
            LastEvaluatedAt = DateTime.UtcNow,
            Category = "Messaging",
            Provider = "Twilio",
            ResourceId = messageSid,
            Service = to,
            SourceRule = "twilio-inbound",
            Metadata = $"from={from};command={commandResult.CommandName}"
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return TwiML(commandResult.Reply);
    }

    private static IResult TwiML(string message) =>
        Results.Content(
            $"""<?xml version="1.0" encoding="UTF-8"?><Response><Message>{System.Security.SecurityElement.Escape(message)}</Message></Response>""",
            "application/xml",
            Encoding.UTF8);

    private static string BuildExternalRequestUrl(HttpRequest request)
    {
        var scheme = request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? request.Scheme;
        var host = request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? request.Host.Value;
        var pathBase = request.PathBase.Value ?? string.Empty;
        var path = request.Path.Value ?? string.Empty;
        var query = request.QueryString.Value ?? string.Empty;
        return $"{scheme}://{host}{pathBase}{path}{query}";
    }

    private static string NormalizePhoneNumber(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("whatsapp:", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["whatsapp:".Length..];
        }

        return trimmed;
    }

    private static string BuildRawPayloadSnapshot(IFormCollection form) =>
        string.Join("&", form.Select(pair => $"{pair.Key}={pair.Value}"));
}

internal static class MessagingCommandProcessor
{
    public static async Task<MessagingCommandResult> ProcessAsync(
        Guid userId,
        string messageBody,
        DracoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var parsed = Parse(messageBody);
        return parsed.Name switch
        {
            "help" => await HandleHelpAsync(userId, dbContext, cancellationToken),
            "status" => await HandleStatusAsync(userId, dbContext, cancellationToken),
            "approve" => await HandleResolutionAsync(userId, parsed.Target, "Completed", "approved", dbContext, cancellationToken),
            "dismiss" => await HandleResolutionAsync(userId, parsed.Target, "Dismissed", "dismissed", dbContext, cancellationToken),
            _ => await HandleUnknownAsync(userId, dbContext, cancellationToken)
        };
    }

    private static async Task<MessagingCommandResult> HandleHelpAsync(
        Guid userId,
        DracoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var openCount = await CountOpenRunsAsync(userId, dbContext, cancellationToken);
        return new MessagingCommandResult(
            "help",
            $"Draco commands: STATUS, APPROVE <id|latest>, DISMISS <id|latest>, HELP. You currently have {openCount} open workflow item{(openCount == 1 ? string.Empty : "s")}.",
            "Sent help response.");
    }

    private static async Task<MessagingCommandResult> HandleStatusAsync(
        Guid userId,
        DracoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var runs = await dbContext.WorkflowRuns
            .AsNoTracking()
            .Where(run => run.UserId == userId && run.Status == "Open")
            .OrderByDescending(run => run.CreatedAt)
            .Take(3)
            .ToListAsync(cancellationToken);

        if (runs.Count == 0)
        {
            return new MessagingCommandResult("status", "Draco status: no open workflow items right now.", "No open workflow items.");
        }

        var summary = string.Join(" | ", runs.Select((run, index) =>
            $"{index + 1}:{ShortId(run.Id)} {run.SuggestedAction} [{run.Severity}]"));

        return new MessagingCommandResult(
            "status",
            $"Draco status: {runs.Count} open workflow item{(runs.Count == 1 ? string.Empty : "s")}. {summary}",
            $"Reported {runs.Count} open workflow items.");
    }

    private static async Task<MessagingCommandResult> HandleResolutionAsync(
        Guid userId,
        string? target,
        string newStatus,
        string verb,
        DracoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var run = await ResolveTargetRunAsync(userId, target, dbContext, cancellationToken);
        if (run is null)
        {
            return new MessagingCommandResult(
                verb,
                $"Draco could not find an open workflow item for '{target ?? "latest"}'. Reply STATUS to list current items.",
                $"No matching workflow item for '{target ?? "latest"}'.",
                "Warning",
                "Medium");
        }

        run.Status = newStatus;
        run.UpdatedAt = DateTimeOffset.UtcNow;
        run.CompletedAt = DateTimeOffset.UtcNow;

        return new MessagingCommandResult(
            verb,
            $"Draco {verb} workflow {ShortId(run.Id)} for {run.SuggestedAction}.",
            $"Workflow {ShortId(run.Id)} marked {newStatus}.",
            newStatus == "Dismissed" ? "Warning" : "Success",
            newStatus == "Dismissed" ? "Medium" : "Info");
    }

    private static async Task<MessagingCommandResult> HandleUnknownAsync(
        Guid userId,
        DracoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var openCount = await CountOpenRunsAsync(userId, dbContext, cancellationToken);
        return new MessagingCommandResult(
            "unknown",
            $"Draco did not recognize that command. Reply HELP for command options. Open workflow items: {openCount}.",
            "Received unknown command.",
            "Warning",
            "Low");
    }

    private static CommandEnvelope Parse(string messageBody)
    {
        var normalized = (messageBody ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new CommandEnvelope("unknown", null);
        }

        var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var name = parts[0].Trim().ToLowerInvariant();
        var target = parts.Length > 1 ? parts[1].Trim() : null;
        return new CommandEnvelope(name, target);
    }

    private static async Task<int> CountOpenRunsAsync(Guid userId, DracoDbContext dbContext, CancellationToken cancellationToken) =>
        await dbContext.WorkflowRuns.CountAsync(run => run.UserId == userId && run.Status == "Open", cancellationToken);

    private static async Task<Draco.Domain.Entities.WorkflowRun?> ResolveTargetRunAsync(
        Guid userId,
        string? target,
        DracoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var openRuns = dbContext.WorkflowRuns
            .Where(run => run.UserId == userId && run.Status == "Open")
            .OrderByDescending(run => run.CreatedAt);

        if (string.IsNullOrWhiteSpace(target) || string.Equals(target, "latest", StringComparison.OrdinalIgnoreCase))
        {
            return await openRuns.FirstOrDefaultAsync(cancellationToken);
        }

        var normalizedTarget = target.Trim();

        if (Guid.TryParse(normalizedTarget, out var workflowId))
        {
            return await openRuns.FirstOrDefaultAsync(run => run.Id == workflowId, cancellationToken);
        }

        if (int.TryParse(normalizedTarget, out var ordinal) && ordinal > 0)
        {
            var rankedCandidates = await openRuns.Take(Math.Max(ordinal, 20)).ToListAsync(cancellationToken);
            return rankedCandidates.ElementAtOrDefault(ordinal - 1);
        }

        var candidates = await openRuns.Take(20).ToListAsync(cancellationToken);
        return candidates.FirstOrDefault(
            run => run.Id.ToString().StartsWith(normalizedTarget, StringComparison.OrdinalIgnoreCase));
    }

    private static string ShortId(Guid id) =>
        id.ToString("N")[..8].ToUpperInvariant();

    private sealed record CommandEnvelope(string Name, string? Target);
}

internal sealed record MessagingCommandResult(
    string CommandName,
    string Reply,
    string Summary,
    string NotificationType = "Info",
    string NotificationSeverity = "Info");

internal static class TwilioRequestValidator
{
    public static bool IsValid(string requestUrl, IFormCollection form, string signature, string authToken)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        var data = requestUrl + string.Concat(
            form.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Key + pair.Value.ToString()));

        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(authToken));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        var computed = Convert.ToBase64String(hash);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed),
            Encoding.UTF8.GetBytes(signature.Trim()));
    }
}
