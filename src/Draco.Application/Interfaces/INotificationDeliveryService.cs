using Draco.Domain.Entities;

namespace Draco.Application.Interfaces;

public interface INotificationDeliveryService
{
    Task DeliverAsync(UserAccount user, SystemNotification notification, CancellationToken cancellationToken = default);
}
