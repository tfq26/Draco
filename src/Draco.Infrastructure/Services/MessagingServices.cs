using Draco.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SendGrid;
using SendGrid.Helpers.Mail;
using Twilio;
using Twilio.Rest.Api.V2010.Account;

namespace Draco.Infrastructure.Services;

public class TwilioService : IMessagingService
{
    private readonly ILogger<TwilioService> _logger;
    private readonly string _accountSid;
    private readonly string _authToken;
    private readonly string _fromNumber;

    public TwilioService(ILogger<TwilioService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _accountSid = (configuration["Twilio:AccountSid"] ?? configuration["TWILIO_ACCOUNT_SID"] ?? "AC123").Trim();
        _authToken = (configuration["Twilio:AuthToken"] ?? configuration["TWILIO_AUTH_TOKEN"] ?? "token").Trim();
        _fromNumber = (configuration["Twilio:FromNumber"] ?? configuration["TWILIO_FROM_NUMBER"] ?? "+123456789").Trim();
        
        TwilioClient.Init(_accountSid, _authToken);
    }

    public async Task SendMessageAsync(string to, string message, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Sending SMS to {To} via Twilio.", to);
        try
        {
            await MessageResource.CreateAsync(
                body: message,
                from: new Twilio.Types.PhoneNumber(_fromNumber),
                to: new Twilio.Types.PhoneNumber(to)
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send SMS to {To}.", to);
            throw; // Re-throw so the UI knows it failed
        }
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
