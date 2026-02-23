using Draco.Application.Interfaces;
using Draco.Domain.Entities;
using Draco.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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

    public async Task GenerateAndSendReportAsync(string phone, CancellationToken ct = default)
    {
        _logger.LogInformation("Generating Pulse Report for {Phone}", phone);

        var resources = await _dbContext.CloudResources
            .AsNoTracking()
            .Where(r => _dbContext.CloudConnections
                .Where(c => c.UserPhone == phone)
                .Select(c => c.SubscriptionId)
                .Contains(r.SubscriptionId))
            .Select(r => new { r.Name, r.Type, r.Location, r.Provider })
            .ToListAsync(ct);

        if (resources.Count == 0)
        {
            _logger.LogWarning("No resources found for user {Phone}. Skipping report.", phone);
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
        var account = await _dbContext.UserAccounts.FindAsync(new object[] { phone }, ct);
        if (account != null)
        {
            var message = $"🚀 Draco Pulse Summary:\n{summary.Substring(0, Math.Min(summary.Length, 1000))}";
            await _messagingService.SendMessageAsync(phone, message, ct);
            _logger.LogInformation("Pulse Report sent to {Phone}", phone);
        }
    }
}
