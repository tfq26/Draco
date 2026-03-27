using Draco.Application.Models;

namespace Draco.Application.Interfaces;

public interface INotificationEvaluationService
{
    Task<NotificationRefreshResult> RefreshUserNotificationsAsync(Guid userId, CancellationToken cancellationToken = default);
}
