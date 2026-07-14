using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Draco.Application.Interfaces;
using Draco.Application.Models;
using Draco.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Draco.Api.Endpoints;

public static class MessagingWebhookEndpoints
{
    public static void MapMessagingWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/webhooks/twilio/messages", HandleIncomingTwilioMessageAsync)
            .WithName("HandleIncomingTwilioMessage");

        app.MapPost("/api/webhook/twilio/messages", HandleIncomingTwilioMessageAsync)
            .WithName("HandleIncomingTwilioMessageLegacy");

        app.MapPost("/api/webhook/vonage", HandleIncomingTwilioMessageAsync)
            .WithName("HandleIncomingTwilioMessageVonageAlias");
    }

    private static async Task<IResult> HandleIncomingTwilioMessageAsync(
        HttpRequest request,
        DracoDbContext dbContext,
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        ILoggerFactory loggerFactory,
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
            return TwiML(BuildSupportErrorMessage(SupportErrorCatalog.EmptyMessage));
        }

        var user = await ResolveUserAccountAsync(from, dbContext, cancellationToken);

        if (user is null)
        {
            return TwiML(BuildSupportErrorMessage(SupportErrorCatalog.UnknownUser));
        }

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
            Message = $"From {from}: {body}",
            Type = "Info",
            Severity = "Info",
            CreatedAt = DateTime.UtcNow,
            LastEvaluatedAt = DateTime.UtcNow,
            Category = "Messaging",
            Provider = "Twilio",
            ResourceId = messageSid,
            Service = to,
            SourceRule = "twilio-inbound",
            Metadata = $"from={from};status=received"
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        _ = Task.Run(async () =>
        {
            await ProcessIncomingMessageAsync(
                user.Id,
                from,
                body,
                messageSid,
                scopeFactory,
                loggerFactory);
        });

        return TwiML("Working on it...");
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

    private static async Task<Draco.Domain.Entities.UserAccount?> ResolveUserAccountAsync(
        string from,
        DracoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var accounts = await dbContext.UserAccounts
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return accounts.FirstOrDefault(account => InboundNumberMatches(account, from));
    }

    private static bool InboundNumberMatches(Draco.Domain.Entities.UserAccount account, string from)
    {
        var normalizedFrom = NormalizeComparablePhone(from);
        if (string.IsNullOrWhiteSpace(normalizedFrom))
        {
            return false;
        }

        if (NormalizeComparablePhone(account.Phone) == normalizedFrom)
        {
            return true;
        }

        return DeserializeRecipients(account.SmsRecipientsJson)
                   .Concat(DeserializeRecipients(account.WhatsAppRecipientsJson))
                   .Any(recipient => NormalizeComparablePhone(recipient) == normalizedFrom);
    }

    private static IEnumerable<string> DeserializeRecipients(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(rawJson) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string? NormalizeComparablePhone(string? rawPhone)
    {
        if (string.IsNullOrWhiteSpace(rawPhone))
        {
            return null;
        }

        var trimmed = rawPhone.Trim();
        if (trimmed.StartsWith("whatsapp:", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["whatsapp:".Length..];
        }

        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        if (digits.Length == 10)
        {
            digits = $"1{digits}";
        }

        return digits.Length >= 11 ? digits : null;
    }

    private static async Task ProcessIncomingMessageAsync(
        Guid userId,
        string from,
        string body,
        string messageSid,
        IServiceScopeFactory scopeFactory,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("MessagingWebhookBackground");

        try
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DracoDbContext>();
            var autonomousInsightService = scope.ServiceProvider.GetRequiredService<IAutonomousInsightService>();
            var messagingService = scope.ServiceProvider.GetRequiredService<IMessagingService>();

            var commandResult = await MessagingCommandProcessor.ProcessAsync(
                userId,
                body,
                dbContext,
                autonomousInsightService,
                CancellationToken.None);

            if (string.IsNullOrWhiteSpace(commandResult.Reply))
            {
                await LogSupportErrorAsync(
                    dbContext,
                    userId,
                    SupportErrorCatalog.EmptyResponse,
                    "No response text was generated for the inbound message.",
                    messageSid,
                    null,
                    CancellationToken.None);

                await messagingService.SendWhatsAppMessageAsync(
                    from,
                    BuildSupportErrorMessage(SupportErrorCatalog.EmptyResponse),
                    CancellationToken.None);

                return;
            }

            var delivered = await messagingService.SendWhatsAppMessageAsync(
                from,
                commandResult.Reply.Trim(),
                CancellationToken.None);

            if (!delivered)
            {
                await LogSupportErrorAsync(
                    dbContext,
                    userId,
                    SupportErrorCatalog.DeliveryFailed,
                    "Twilio rejected or failed the outbound WhatsApp reply.",
                    messageSid,
                    JsonSerializer.Serialize(new
                    {
                        to = from,
                        commandResult.CommandName
                    }),
                    CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Background WhatsApp processing failed for user {UserId}.", userId);

            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DracoDbContext>();
            var messagingService = scope.ServiceProvider.GetRequiredService<IMessagingService>();

            await LogSupportErrorAsync(
                dbContext,
                userId,
                SupportErrorCatalog.GenericProcessingFailure,
                ex.Message,
                messageSid,
                JsonSerializer.Serialize(new
                {
                    exception = ex.GetType().FullName,
                    ex.StackTrace
                }),
                CancellationToken.None);

            await messagingService.SendWhatsAppMessageAsync(
                from,
                BuildSupportErrorMessage(SupportErrorCatalog.GenericProcessingFailure),
                CancellationToken.None);
        }
    }

    private static async Task LogSupportErrorAsync(
        DracoDbContext dbContext,
        Guid userId,
        string errorCode,
        string summary,
        string? correlationId,
        string? metadata,
        CancellationToken cancellationToken)
    {
        var definition = SupportErrorCatalog.Find(errorCode);
        var title = definition?.Title ?? errorCode;
        var eventPayload = JsonSerializer.Serialize(new
        {
            errorCode,
            summary,
            metadata
        });

        dbContext.WorkflowEvents.Add(new Draco.Domain.Entities.WorkflowEvent
        {
            UserId = userId,
            Source = "Messaging",
            EventType = "SupportError",
            Category = "Support",
            Severity = "High",
            Provider = "Twilio",
            SubscriptionId = string.Empty,
            Title = $"{title} ({errorCode})",
            Summary = summary,
            Status = "Logged",
            OccurredAt = DateTimeOffset.UtcNow,
            ReceivedAt = DateTimeOffset.UtcNow,
            CorrelationId = correlationId,
            RawPayload = eventPayload,
            ProcessingError = summary
        });

        dbContext.SystemNotifications.Add(new Draco.Domain.Entities.SystemNotification
        {
            UserId = userId,
            NotificationKey = $"support-error:{errorCode}:{correlationId ?? Guid.NewGuid().ToString("N")}",
            Title = $"{title} ({errorCode})",
            Message = summary,
            Type = "Error",
            Severity = "High",
            CreatedAt = DateTime.UtcNow,
            LastEvaluatedAt = DateTime.UtcNow,
            Category = "Support",
            Provider = "Twilio",
            ResourceId = correlationId,
            Service = "Messaging",
            SourceRule = "support-error",
            Metadata = eventPayload
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string BuildSupportErrorMessage(string errorCode)
    {
        var definition = SupportErrorCatalog.Find(errorCode);
        return definition?.UserMessage ?? $"There was a problem getting a response. Error code: {errorCode}.";
    }
}

internal static class MessagingCommandProcessor
{
    public static async Task<MessagingCommandResult> ProcessAsync(
        Guid userId,
        string messageBody,
        DracoDbContext dbContext,
        IAutonomousInsightService autonomousInsightService,
        CancellationToken cancellationToken)
    {
        var parsed = Parse(messageBody);
        return parsed.Name switch
        {
            "help" => await HandleHelpAsync(userId, dbContext, cancellationToken),
            "status" => await HandleStatusAsync(userId, dbContext, cancellationToken),
            "approve" => await HandleResolutionAsync(userId, parsed.Target, "Completed", "approved", dbContext, cancellationToken),
            "dismiss" => await HandleResolutionAsync(userId, parsed.Target, "Dismissed", "dismissed", dbContext, cancellationToken),
            _ => await HandleAiQueryAsync(userId, messageBody, dbContext, autonomousInsightService, cancellationToken)
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

    private static async Task<MessagingCommandResult> HandleAiQueryAsync(
        Guid userId,
        string messageBody,
        DracoDbContext dbContext,
        IAutonomousInsightService autonomousInsightService,
        CancellationToken cancellationToken)
    {
        var response = await autonomousInsightService.AnswerUserQueryAsync(userId, messageBody, cancellationToken);
        if (response is not null && !string.IsNullOrWhiteSpace(response.Narrative))
        {
            return new MessagingCommandResult(
                "ai-query",
                response.Narrative.Trim(),
                $"Answered AI query: {messageBody.Trim()}",
                "Info",
                "Info");
        }

        var openCount = await CountOpenRunsAsync(userId, dbContext, cancellationToken);
        return new MessagingCommandResult(
            "ai-query-fallback",
            $"Draco could not answer that just yet. Reply HELP for command options. Open workflow items: {openCount}.",
            "AI query fallback response returned.",
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
