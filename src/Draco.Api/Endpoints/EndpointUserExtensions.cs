using System.Security.Claims;
using Draco.Domain.Entities;
using Draco.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Draco.Api.Endpoints;

internal static class EndpointUserExtensions
{
    public static string? GetSubject(this ClaimsPrincipal principal) =>
        principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? principal.FindFirst("sub")?.Value;

    public static Task<UserAccount?> GetCurrentUserAsync(
        this ClaimsPrincipal principal,
        DracoDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        var subject = principal.GetSubject();
        if (string.IsNullOrWhiteSpace(subject))
        {
            return Task.FromResult<UserAccount?>(null);
        }

        return dbContext.UserAccounts
            .AsSplitQuery()
            .Include(user => user.Connections)
            .Include(user => user.ReportSchedules)
            .FirstOrDefaultAsync(
                user => user.Id.ToString() == subject || user.AuthId == subject,
                cancellationToken);
    }
}
