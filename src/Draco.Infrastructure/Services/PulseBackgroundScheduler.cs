using Draco.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Draco.Infrastructure.Services;

public class PulseBackgroundScheduler : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<PulseBackgroundScheduler> _logger;

    public PulseBackgroundScheduler(IServiceProvider services, ILogger<PulseBackgroundScheduler> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Pulse Background Scheduler is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<DracoDbContext>();
                var reportService = scope.ServiceProvider.GetRequiredService<PulseReportService>();

                var now = DateTimeOffset.UtcNow;
                var pendingSchedules = await dbContext.PulseReportSchedules
                    .Where(s => s.IsActive && s.NextRunAt <= now)
                    .ToListAsync(stoppingToken);

                foreach (var schedule in pendingSchedules)
                {
                    await reportService.GenerateAndSendReportAsync(schedule.UserPhone, stoppingToken);

                    schedule.LastSentAt = now;
                    schedule.NextRunAt = schedule.Frequency switch
                    {
                        "Daily" => now.AddDays(1),
                        "Weekly" => now.AddDays(7),
                        "Monthly" => now.AddMonths(1),
                        _ => now.AddDays(7)
                    };
                }

                await dbContext.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking pulse schedules.");
            }

            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken); // Check every 15 mins
        }
    }
}
