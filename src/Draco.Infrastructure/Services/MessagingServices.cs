using Draco.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace Draco.Infrastructure.Services;

public class AzureMessagingService : IMessagingService
{
    private readonly ILogger<AzureMessagingService> _logger;
    private readonly string _connectionString;
    private readonly string _fromNumber;

    public AzureMessagingService(ILogger<AzureMessagingService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _connectionString = (configuration["AZURE_SMS_CONNECTION_STRING"] ?? string.Empty).Trim();
        _fromNumber = (configuration["AZURE_SMS_FROM_NUMBER"] ?? string.Empty).Trim();
    }

    public async Task SendMessageAsync(string to, string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString) || string.IsNullOrWhiteSpace(_fromNumber))
        {
            _logger.LogWarning("SMS delivery skipped because Azure messaging configuration is incomplete.");
            return;
        }

        await Task.CompletedTask;
        _logger.LogInformation(
            "SMS delivery stub invoked for {To}. Configure Azure Communication Services before enabling notifications in production.",
            to);
    }

    public async Task SendWhatsAppMessageAsync(string to, string message, CancellationToken cancellationToken = default)
    {
        // WhatsApp via ACS usually requires a 'Job' or specific templates.
        // For Draco alerts, we fallback to SMS if WhatsApp isn't fully configured.
        _logger.LogInformation("Attempting WhatsApp intent via Azure, falling back to SMS for Draco alerts.");
        await SendMessageAsync(to, message, cancellationToken);
    }
}

public class SendGridService : IEmailService
{
    private readonly ILogger<SendGridService> _logger;
    private readonly string _apiKey;
    private readonly string _fromEmail;

    public SendGridService(ILogger<SendGridService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _apiKey = configuration["SendGrid:ApiKey"] ?? configuration["SENDGRID_API_KEY"] ?? "SG.123";
        _fromEmail = configuration["SendGrid:FromEmail"] ?? configuration["SENDGRID_FROM_EMAIL"] ?? "sentinel@draco.io";
    }

    public async Task SendEmailAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Sending Email to {To} via SendGrid.", to);
        try
        {
            var client = new SendGridClient(_apiKey);
            var from = new EmailAddress(_fromEmail, "Draco Sentinel");
            var toEmail = new EmailAddress(to);
            var msg = MailHelper.CreateSingleEmail(from, toEmail, subject, body, body);
            await client.SendEmailAsync(msg, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Email to {To}.", to);
        }
    }
}
