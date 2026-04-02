using Draco.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SendGrid;
using SendGrid.Helpers.Mail;
using System.Net.Http.Headers;
using System.Text;

namespace Draco.Infrastructure.Services;

public class TwilioMessagingService : IMessagingService
{
    private readonly ILogger<TwilioMessagingService> _logger;
    private readonly string _accountSid;
    private readonly string _authToken;
    private readonly string _smsFromNumber;
    private readonly string _whatsAppFromNumber;

    public TwilioMessagingService(
        ILogger<TwilioMessagingService> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _accountSid = (configuration["Twilio:AccountSid"] ?? configuration["TWILIO_ACCOUNT_SID"] ?? string.Empty).Trim();
        _authToken = (configuration["Twilio:AuthToken"] ?? configuration["TWILIO_AUTH_TOKEN"] ?? string.Empty).Trim();
        _smsFromNumber = (configuration["Twilio:SmsFromNumber"] ?? configuration["TWILIO_SMS_FROM_NUMBER"] ?? configuration["TWILIO_FROM_NUMBER"] ?? string.Empty).Trim();
        _whatsAppFromNumber = (configuration["Twilio:WhatsAppFromNumber"] ?? configuration["TWILIO_WHATSAPP_FROM_NUMBER"] ?? string.Empty).Trim();
    }

    public async Task SendMessageAsync(string to, string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_accountSid) ||
            string.IsNullOrWhiteSpace(_authToken) ||
            string.IsNullOrWhiteSpace(_smsFromNumber))
        {
            _logger.LogWarning("SMS delivery skipped because Twilio SMS configuration is incomplete.");
            return;
        }

        await SendTwilioMessageAsync(NormalizePhoneAddress(to), NormalizePhoneAddress(_smsFromNumber), message, "SMS", cancellationToken);
    }

    public async Task SendWhatsAppMessageAsync(string to, string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_accountSid) || string.IsNullOrWhiteSpace(_authToken))
        {
            _logger.LogWarning("WhatsApp delivery skipped because Twilio credentials are incomplete.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_whatsAppFromNumber))
        {
            _logger.LogInformation("Twilio WhatsApp sender is not configured. Falling back to SMS delivery for {To}.", to);
            await SendMessageAsync(to, message, cancellationToken);
            return;
        }

        await SendTwilioMessageAsync(
            NormalizeWhatsAppAddress(to),
            NormalizeWhatsAppAddress(_whatsAppFromNumber),
            message,
            "WhatsApp",
            cancellationToken);
    }

    private async Task SendTwilioMessageAsync(
        string to,
        string from,
        string body,
        string channel,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient();
            client.BaseAddress = new Uri("https://api.twilio.com/");

            var authBytes = Encoding.ASCII.GetBytes($"{_accountSid}:{_authToken}");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(authBytes));

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["To"] = to,
                ["From"] = from,
                ["Body"] = body
            });

            var response = await client.PostAsync(
                $"2010-04-01/Accounts/{Uri.EscapeDataString(_accountSid)}/Messages.json",
                content,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Sent {Channel} message via Twilio to {To}.", channel, to);
                return;
            }

            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "Failed to send {Channel} message via Twilio to {To}. Status: {StatusCode}. Response: {Response}",
                channel,
                to,
                (int)response.StatusCode,
                errorBody);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send {Channel} message via Twilio to {To}.", channel, to);
        }
    }

    private static string NormalizePhoneAddress(string value) =>
        value.Trim();

    private static string NormalizeWhatsAppAddress(string value)
    {
        var normalized = value.Trim();
        return normalized.StartsWith("whatsapp:", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : $"whatsapp:{normalized}";
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
