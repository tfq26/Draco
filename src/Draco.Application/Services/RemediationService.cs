using Draco.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Draco.Application.Services;

public class RemediationService
{
    private readonly IAIService _aiService;
    private readonly AlertOrchestrator _alertOrchestrator;
    private readonly IGitProvider _gitProvider;
    private readonly ILogger<RemediationService> _logger;

    public RemediationService(
        IAIService aiService,
        AlertOrchestrator alertOrchestrator,
        IGitProvider gitProvider,
        ILogger<RemediationService> logger)
    {
        _aiService = aiService;
        _alertOrchestrator = alertOrchestrator;
        _gitProvider = gitProvider;
        _logger = logger;
    }

    public async Task ProcessAnomalyAsync(string rawData, string phone, string email, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing new cloud anomaly...");

        // 1. Analyze with AI
        var analysis = await _aiService.AnalyzeAnomalyAsync(rawData, cancellationToken);
        _logger.LogDebug("AI Analysis: {Analysis}", analysis);

        // 2. Create Conversational Alert
        var alertMessage = await _aiService.CreateConversationalAlertAsync(analysis, cancellationToken);

        // 3. Notify User
        await _alertOrchestrator.AlertAnomalyAsync(alertMessage, phone, email, cancellationToken);
    }

    public async Task RemediateAsync(string context, string repoOwner, string repoName, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Remediation triggered for: {Context}", context);

        // 1. Generate HCL
        var hcl = await _aiService.GenerateRemediationHclAsync(context, cancellationToken);
        _logger.LogInformation("Generated HCL: {Hcl}", hcl);

        // 2. Create Git PR
        var files = new Dictionary<string, string>
        {
            { "terraform/remediation.tf", hcl }
        };

        var branchName = $"draco-remediation-{Guid.NewGuid().ToString()[..8]}";
        await _gitProvider.CreatePullRequestAsync(
            repoOwner, 
            repoName, 
            branchName, 
            "Draco: Autonomous Remediation", 
            $"Draco detected an anomaly and generated this fix: \n\n {context}", 
            files, 
            cancellationToken);
    }
}
