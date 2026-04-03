using Draco.Domain.Entities;

namespace Draco.Application.Interfaces;

public interface INotificationDeliveryService
{
    Task<bool> DeliverAsync(UserAccount user, SystemNotification notification, CancellationToken cancellationToken = default);
}
