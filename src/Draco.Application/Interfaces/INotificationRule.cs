using Draco.Application.Models;
using Draco.Domain.Entities;

namespace Draco.Application.Interfaces;

public interface INotificationRule
{
    string RuleId { get; }
    IEnumerable<string> GetRequiredMetricNames(CloudResource resource);
    IEnumerable<NotificationCandidate> Evaluate(NotificationEvaluationContext context);
}
