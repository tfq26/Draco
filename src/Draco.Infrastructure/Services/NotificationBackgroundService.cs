using Draco.Application.Interfaces;
using Draco.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Draco.Infrastructure.Services;

public sealed class NotificationBackgroundService : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(15);

    private readonly IServiceProvider _services;
    private readonly ILogger<NotificationBackgroundService> _logger;

    public NotificationBackgroundService(
        IServiceProvider services,
        ILogger<NotificationBackgroundService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Notification background service is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<DracoDbContext>();
                var notificationEvaluationService = scope.ServiceProvider.GetRequiredService<INotificationEvaluationService>();

                var userIds = await dbContext.CloudConnections
                    .AsNoTracking()
                    .Where(connection => connection.IsActive)
                    .Select(connection => connection.UserId)
                    .Distinct()
                    .ToListAsync(stoppingToken);

                foreach (var userId in userIds)
                {
                    try
                    {
                        await notificationEvaluationService.RefreshUserNotificationsAsync(userId, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Notification refresh failed for user {UserId}.", userId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background notification refresh loop failed.");
            }

            await Task.Delay(RefreshInterval, stoppingToken);
        }
    }
}
