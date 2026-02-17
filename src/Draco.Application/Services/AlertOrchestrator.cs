using Draco.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Draco.Application.Services;

public class AlertOrchestrator
{
    private readonly IMessagingService _messagingService;
    private readonly IEmailService _emailService;
    private readonly ILogger<AlertOrchestrator> _logger;

    public AlertOrchestrator(
        IMessagingService messagingService,
        IEmailService emailService,
        ILogger<AlertOrchestrator> logger)
    {
        _messagingService = messagingService;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task AlertAnomalyAsync(string anomalyDescription, string userPhoneNumber, string userEmail, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Orchestrating alerts for anomaly: {Anomaly}", anomalyDescription);

        // 1. Send immediate SMS for action
        await _messagingService.SendMessageAsync(userPhoneNumber, $"Draco Sentinel: {anomalyDescription}. Reply 'Apply' to remediate.", cancellationToken);

        // 2. Send detailed Email report
        await _emailService.SendEmailAsync(userEmail, "Draco Alert: Cloud Anomaly Detected", $"<h3>Draco Sentinel Analysis</h3><p>{anomalyDescription}</p>", cancellationToken);
    }
}
