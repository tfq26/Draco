namespace Draco.Application.Interfaces;

public interface IMessagingService
{
    Task SendMessageAsync(string to, string message, CancellationToken cancellationToken = default);
}

public interface IEmailService
{
    Task SendEmailAsync(string to, string subject, string body, CancellationToken cancellationToken = default);
}
