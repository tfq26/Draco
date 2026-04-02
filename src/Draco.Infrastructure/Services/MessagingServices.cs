using Draco.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SendGrid;
using SendGrid.Helpers.Mail;
<<<<<<< HEAD
=======
using System.Net.Http;
>>>>>>> c4bc3d5 (Add multi-recipient Twilio delivery for SMS and WhatsApp)
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
<<<<<<< HEAD
        _accountSid = (configuration["Twilio:AccountSid"] ?? configuration["TWILIO_ACCOUNT_SID"] ?? string.Empty).Trim();
        _authToken = (configuration["Twilio:AuthToken"] ?? configuration["TWILIO_AUTH_TOKEN"] ?? string.Empty).Trim();
        _smsFromNumber = (configuration["Twilio:SmsFromNumber"] ?? configuration["TWILIO_SMS_FROM_NUMBER"] ?? configuration["TWILIO_FROM_NUMBER"] ?? string.Empty).Trim();
        _whatsAppFromNumber = (configuration["Twilio:WhatsAppFromNumber"] ?? configuration["TWILIO_WHATSAPP_FROM_NUMBER"] ?? string.Empty).Trim();
=======
        _accountSid = (configuration["TWILIO_ACCOUNT_SID"] ?? string.Empty).Trim();
        _authToken = (configuration["TWILIO_AUTH_TOKEN"] ?? string.Empty).Trim();
        _smsFromNumber = (configuration["TWILIO_SMS_FROM_NUMBER"] ?? string.Empty).Trim();
        _whatsAppFromNumber = (configuration["TWILIO_WHATSAPP_FROM_NUMBER"] ?? string.Empty).Trim();
>>>>>>> c4bc3d5 (Add multi-recipient Twilio delivery for SMS and WhatsApp)
    }

    public async Task SendMessageAsync(string to, string message, CancellationToken cancellationToken = default)
    {
<<<<<<< HEAD
        if (string.IsNullOrWhiteSpace(_accountSid) ||
            string.IsNullOrWhiteSpace(_authToken) ||
            string.IsNullOrWhiteSpace(_smsFromNumber))
=======
        if (!HasTwilioCredentials() || string.IsNullOrWhiteSpace(_smsFromNumber))
>>>>>>> c4bc3d5 (Add multi-recipient Twilio delivery for SMS and WhatsApp)
        {
            _logger.LogWarning("SMS delivery skipped because Twilio SMS configuration is incomplete.");
            return;
        }

<<<<<<< HEAD
        await SendTwilioMessageAsync(NormalizePhoneAddress(to), NormalizePhoneAddress(_smsFromNumber), message, "SMS", cancellationToken);
=======
        var normalizedTo = NormalizePhoneNumber(to);
        if (string.IsNullOrWhiteSpace(normalizedTo))
        {
            _logger.LogWarning("SMS delivery skipped because recipient phone number was invalid: {To}", to);
            return;
        }

        await SendTwilioMessageAsync(_smsFromNumber, normalizedTo, message, "SMS", cancellationToken);
>>>>>>> c4bc3d5 (Add multi-recipient Twilio delivery for SMS and WhatsApp)
    }

    public async Task SendWhatsAppMessageAsync(string to, string message, CancellationToken cancellationToken = default)
    {
<<<<<<< HEAD
        if (string.IsNullOrWhiteSpace(_accountSid) || string.IsNullOrWhiteSpace(_authToken))
        {
            _logger.LogWarning("WhatsApp delivery skipped because Twilio credentials are incomplete.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_whatsAppFromNumber))
        {
            _logger.LogInformation("Twilio WhatsApp sender is not configured. Falling back to SMS delivery for {To}.", to);
            await SendMessageAsync(to, message, cancellationToken);
=======
        if (!HasTwilioCredentials() || string.IsNullOrWhiteSpace(_whatsAppFromNumber))
        {
            _logger.LogWarning("WhatsApp delivery skipped because Twilio WhatsApp configuration is incomplete.");
            return;
        }

        var normalizedTo = NormalizePhoneNumber(to);
        if (string.IsNullOrWhiteSpace(normalizedTo))
        {
            _logger.LogWarning("WhatsApp delivery skipped because recipient phone number was invalid: {To}", to);
>>>>>>> c4bc3d5 (Add multi-recipient Twilio delivery for SMS and WhatsApp)
            return;
        }

        await SendTwilioMessageAsync(
<<<<<<< HEAD
            NormalizeWhatsAppAddress(to),
            NormalizeWhatsAppAddress(_whatsAppFromNumber),
=======
            NormalizeWhatsAppHandle(_whatsAppFromNumber),
            NormalizeWhatsAppHandle(normalizedTo),
>>>>>>> c4bc3d5 (Add multi-recipient Twilio delivery for SMS and WhatsApp)
            message,
            "WhatsApp",
            cancellationToken);
    }

<<<<<<< HEAD
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
=======
    private bool HasTwilioCredentials() =>
        !string.IsNullOrWhiteSpace(_accountSid) &&
        !string.IsNullOrWhiteSpace(_authToken);

    private async Task SendTwilioMessageAsync(
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
            return;
        }

        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogError(
            "Twilio rejected {Channel} delivery to {To}. Status: {StatusCode}. Response: {ResponseBody}",
            channel,
            to,
            (int)response.StatusCode,
            errorBody);
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
>>>>>>> c4bc3d5 (Add multi-recipient Twilio delivery for SMS and WhatsApp)
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
