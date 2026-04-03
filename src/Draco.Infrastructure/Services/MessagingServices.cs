using Draco.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SendGrid;
using SendGrid.Helpers.Mail;
using System.Net.Http;
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

    public async Task<bool> SendMessageAsync(string to, string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_accountSid) ||
            string.IsNullOrWhiteSpace(_authToken) ||
            string.IsNullOrWhiteSpace(_smsFromNumber))
        {
            _logger.LogWarning("SMS delivery skipped because Twilio SMS configuration is incomplete.");
            return false;
        }

        var normalizedTo = NormalizePhoneNumber(to);
        if (string.IsNullOrWhiteSpace(normalizedTo))
        {
            _logger.LogWarning("SMS delivery skipped because recipient phone number was invalid: {To}", to);
            return false;
        }

        return await SendTwilioMessageAsync(_smsFromNumber, normalizedTo, message, "SMS", cancellationToken);
    }

    public async Task<bool> SendWhatsAppMessageAsync(string to, string message, CancellationToken cancellationToken = default)
    {
        if (!HasTwilioCredentials() || string.IsNullOrWhiteSpace(_whatsAppFromNumber))
        {
            _logger.LogWarning("WhatsApp delivery skipped because Twilio WhatsApp configuration is incomplete.");
            return false;
        }

        var normalizedTo = NormalizePhoneNumber(to);
        if (string.IsNullOrWhiteSpace(normalizedTo))
        {
            _logger.LogWarning("WhatsApp delivery skipped because recipient phone number was invalid: {To}", to);
            return false;
        }

        return await SendTwilioMessageAsync(
            NormalizeWhatsAppHandle(_whatsAppFromNumber),
            NormalizeWhatsAppHandle(normalizedTo),
            message,
            "WhatsApp",
            cancellationToken);
    }

    private bool HasTwilioCredentials() =>
        !string.IsNullOrWhiteSpace(_accountSid) &&
        !string.IsNullOrWhiteSpace(_authToken);

    private async Task<bool> SendTwilioMessageAsync(
        string from,
        string to,
        string message,
        string channel,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        var authBytes = Encoding.ASCII.GetBytes($"{_accountSid}:{_authToken}");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["From"] = from,
            ["To"] = to,
            ["Body"] = message
        });

        var response = await client.PostAsync(
            $"https://api.twilio.com/2010-04-01/Accounts/{_accountSid}/Messages.json",
            content,
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("{Channel} delivery accepted by Twilio for {To}", channel, to);
            return true;
        }

        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogError(
            "Twilio rejected {Channel} delivery to {To}. Status: {StatusCode}. Response: {ResponseBody}",
            channel,
            to,
            (int)response.StatusCode,
            errorBody);
        return false;
    }

    private static string? NormalizePhoneNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("whatsapp:", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["whatsapp:".Length..];
        }

        if (trimmed.StartsWith('+'))
        {
            var digits = new string(trimmed.Where(char.IsDigit).ToArray());
            return digits.Length >= 10 ? $"+{digits}" : null;
        }

        var normalizedDigits = new string(trimmed.Where(char.IsDigit).ToArray());

        return normalizedDigits.Length switch
        {
            10 => $"+1{normalizedDigits}",
            11 when normalizedDigits.StartsWith('1') => $"+{normalizedDigits}",
            >= 10 => $"+{normalizedDigits}",
            _ => null
        };
    }

    private static string NormalizeWhatsAppHandle(string value)
    {
        if (value.StartsWith("whatsapp:", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        return $"whatsapp:{value}";
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
