using Draco.Application.Interfaces;
using Draco.Domain.Entities;
using Draco.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Draco.Infrastructure.Services;

public class PulseReportService
{
    private readonly IAIService _aiService;
    private readonly IMessagingService _messagingService;
    private readonly IEmailService _emailService;
    private readonly DracoDbContext _dbContext;
    private readonly ILogger<PulseReportService> _logger;

    public PulseReportService(
        IAIService aiService,
        IMessagingService messagingService,
        IEmailService emailService,
        DracoDbContext dbContext,
        ILogger<PulseReportService> logger)
    {
        _aiService = aiService;
        _messagingService = messagingService;
        _emailService = emailService;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task GenerateAndSendReportAsync(Guid userId, CancellationToken ct = default)
    {
        _logger.LogInformation("Generating Pulse Report for User {UserId}", userId);

        var resources = await _dbContext.CloudResources
            .AsNoTracking()
            .Where(r => _dbContext.CloudConnections
                .Where(c => c.UserId == userId)
                .Select(c => c.SubscriptionId)
                .Contains(r.SubscriptionId))
            .Select(r => new { r.Name, r.Type, r.Location, r.Provider })
            .ToListAsync(ct);

        if (resources.Count == 0)
        {
            _logger.LogWarning("No resources found for user {UserId}. Skipping report.", userId);
            return;
        }

        var prompt = $@"
Analyze the following cloud resource metadata and provide a executive summary for the user.
The summary should include:
1. Overall Resource Health: A high-level status of their infrastructure.
2. Short-term Concerns: Immediate security or stability issues found in tags or types.
3. Long-term Planning: Suggestions for scaling or architectural changes.
4. Cost Optimization: Specific suggestions to lower costs (e.g., identifying potentially over-provisioned resources).

RESOURCES:
{string.Join("\n", resources.Select(r => $"- {r.Name} ({r.Type}) in {r.Location}"))}
";

        var summary = await _aiService.AnalyzeResourcesAsync(resources.Cast<object>(), prompt);

        // Send summary via email/SMS
        var account = await _dbContext.UserAccounts.FindAsync(new object[] { userId }, ct);
        if (account != null)
        {
            var channels = ParsePreferredChannels(account.PreferredChannel);
            var smsRecipients = ParseRecipients(account.SmsRecipientsJson, account.Phone);
            var whatsAppRecipients = ParseRecipients(account.WhatsAppRecipientsJson);
            if (smsRecipients.Count == 0 && whatsAppRecipients.Count == 0)
            {
                _logger.LogInformation("Skipping Pulse Report delivery for user {UserId} because no recipients are configured.", userId);
                return;
            }

            var message = $"🚀 Draco Pulse Summary:\n{summary.Substring(0, Math.Min(summary.Length, 1000))}";

            if (channels.Contains("SMS"))
            {
                foreach (var recipient in smsRecipients)
                {
                    await _messagingService.SendMessageAsync(recipient, message, ct);
                }
            }

            if (channels.Contains("WhatsApp"))
            {
                foreach (var recipient in whatsAppRecipients)
                {
                    await _messagingService.SendWhatsAppMessageAsync(recipient, message, ct);
                }
            }

            _logger.LogInformation("Pulse Report sent through {Channels}", string.Join(", ", channels));
        }
    }

    private static HashSet<string> ParsePreferredChannels(string? rawChannels)
    {
        var channels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(rawChannels))
        {
            channels.Add("SMS");
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

        if (channels.Count == 0)
        {
            channels.Add("SMS");
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
}
