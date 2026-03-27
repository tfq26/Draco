using Draco.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Draco.Infrastructure.Services;

public class WorkflowEventBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WorkflowEventBackgroundService> _logger;

    public WorkflowEventBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<WorkflowEventBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Workflow event background service is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<DracoDbContext>();
                var workflowEventService = scope.ServiceProvider.GetRequiredService<WorkflowEventService>();

                var pendingEventIds = await dbContext.WorkflowEvents
                    .Where(item => item.Status == "Pending")
                    .OrderBy(item => item.ReceivedAt)
                    .Select(item => item.Id)
                    .Take(20)
                    .ToListAsync(stoppingToken);

                foreach (var eventId in pendingEventIds)
                {
                    await workflowEventService.ProcessPendingEventAsync(eventId, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing background workflow events.");
            }

            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }
}
