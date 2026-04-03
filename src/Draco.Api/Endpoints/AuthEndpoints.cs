using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Draco.Application.Models;
using Draco.Domain.Entities;
using Draco.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Draco.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/workos/sync", SyncWorkOsAccountAsync)
            .WithName("SyncWorkOsAccount");

        group.MapPost("/workos/exchange", ExchangeWorkOsCodeAsync)
            .WithName("ExchangeWorkOsCode");

        group.MapGet("/me", GetCurrentUserAsync)
            .RequireAuthorization()
            .WithName("GetCurrentUser");

        group.MapPost("/setup-complete", CompleteSetupAsync)
            .RequireAuthorization()
            .WithName("CompleteSetup");
    }

    private static async Task<IResult> ExchangeWorkOsCodeAsync(
        [FromBody] WorkOsExchangeRequest request,
        DracoDbContext dbContext,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return Results.BadRequest(new { message = "Authentication code is required." });
        }

        var clientId = configuration["WORKOS_CLIENT_ID"];
        var apiKey = configuration["WORKOS_API_KEY"];
        var logger = loggerFactory.CreateLogger("WorkOsExchange");

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogError("WorkOS configuration is missing. WORKOS_CLIENT_ID or WORKOS_API_KEY is not set.");
            return Results.Problem("WorkOS is not configured correctly on the API.", statusCode: StatusCodes.Status500InternalServerError);
        }

        using var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        var response = await client.PostAsJsonAsync("https://api.workos.com/user_management/authenticate", new
        {
            client_id = clientId,
            grant_type = "authorization_code",
            code = request.Code,
            code_verifier = request.CodeVerifier,
            ip_address = httpContext.Connection.RemoteIpAddress?.ToString(),
            user_agent = httpContext.Request.Headers.UserAgent.ToString()
        }, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("WorkOS code exchange failed with status {StatusCode}: {ErrorContent}", (int)response.StatusCode, errorContent);
            return Results.BadRequest(new { message = "WorkOS exchange failed.", details = errorContent });
        }

        var workOsResponse = await response.Content.ReadFromJsonAsync<WorkOsAuthResponse>(cancellationToken: cancellationToken);
        if (workOsResponse?.user == null)
        {
            return Results.BadRequest(new { message = "WorkOS user data missing in response." });
        }

        var workOsUser = workOsResponse.user;
        var normalizedEmail = workOsUser.email.Trim().ToLowerInvariant();
        
        var account = await dbContext.UserAccounts
            .AsSplitQuery()
            .Include(u => u.Connections)
            .Include(u => u.ReportSchedules)
            .FirstOrDefaultAsync(u => u.AuthId == workOsUser.id || u.Email == normalizedEmail, cancellationToken);

        if (account is null)
        {
            account = new UserAccount
            {
                AuthId = workOsUser.id,
                Email = normalizedEmail,
                Name = $"{workOsUser.first_name} {workOsUser.last_name}".Trim() != "" 
                    ? $"{workOsUser.first_name} {workOsUser.last_name}".Trim() 
                    : normalizedEmail,
                ImageUrl = workOsUser.profile_picture_url,
                CreatedAt = DateTimeOffset.UtcNow,
                LastSeenAt = DateTimeOffset.UtcNow,
                PreferredChannel = NotificationChannelNames.Messages
            };
            dbContext.UserAccounts.Add(account);
        }
        else
        {
            account.AuthId = workOsUser.id;
            account.Email = normalizedEmail;
            account.Name = string.IsNullOrWhiteSpace(workOsUser.first_name) 
                ? account.Name 
                : $"{workOsUser.first_name} {workOsUser.last_name}".Trim();
            account.ImageUrl = workOsUser.profile_picture_url ?? account.ImageUrl;
            account.LastSeenAt = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        var token = GenerateJwtForUser(account, configuration);

        return Results.Ok(new { token, user = ToUserDto(account) });
    }

    private static async Task<IResult> SyncWorkOsAccountAsync(
        [FromBody] WorkOsSyncRequest request,
        DracoDbContext dbContext,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.WorkOsUserId) || string.IsNullOrWhiteSpace(request.Email))
        {
            return Results.BadRequest(new { message = "WorkOS user ID and email are required." });
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var account = await dbContext.UserAccounts
            .AsSplitQuery()
            .Include(user => user.Connections)
            .Include(user => user.ReportSchedules)
            .FirstOrDefaultAsync(
                user => user.AuthId == request.WorkOsUserId || user.Email == normalizedEmail,
                cancellationToken);

        if (account is null)
        {
            account = new UserAccount
            {
                AuthId = request.WorkOsUserId,
                Email = normalizedEmail,
                Name = request.Name?.Trim() ?? normalizedEmail,
                ImageUrl = request.ImageUrl,
                CreatedAt = DateTimeOffset.UtcNow,
                LastSeenAt = DateTimeOffset.UtcNow,
                PreferredChannel = NotificationChannelNames.Messages
            };

            dbContext.UserAccounts.Add(account);
        }
        else
        {
            account.AuthId = request.WorkOsUserId;
            account.Email = normalizedEmail;
            account.Name = string.IsNullOrWhiteSpace(request.Name) ? account.Name : request.Name.Trim();
            account.ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? account.ImageUrl : request.ImageUrl;
            account.LastSeenAt = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var token = GenerateJwtForUser(account, configuration);
        return Results.Ok(new
        {
            token,
            user = ToUserDto(account)
        });
    }

    private static async Task<IResult> GetCurrentUserAsync(
        ClaimsPrincipal userPrincipal,
        DracoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var account = await userPrincipal.GetCurrentUserAsync(dbContext, cancellationToken);
        return account is null
            ? Results.Unauthorized()
            : Results.Ok(ToUserDto(account));
    }

    private static async Task<IResult> CompleteSetupAsync(
        [FromBody] SetupCompleteRequest request,
        ClaimsPrincipal userPrincipal,
        DracoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var account = await userPrincipal.GetCurrentUserAsync(dbContext, cancellationToken);
        if (account is null)
        {
            return Results.Unauthorized();
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            account.Name = request.Name.Trim();
        }

        account.Phone = string.IsNullOrWhiteSpace(request.Phone) ? account.Phone : request.Phone.Trim();
        account.PreferredChannel = string.IsNullOrWhiteSpace(request.PreferredChannel)
            ? account.PreferredChannel
            : request.PreferredChannel.Trim();
        if (request.SmsRecipients is not null)
        {
            account.SmsRecipientsJson = SerializeRecipients(request.SmsRecipients);
        }

        if (request.WhatsAppRecipients is not null)
        {
            account.WhatsAppRecipientsJson = SerializeRecipients(request.WhatsAppRecipients);
        }
        account.LastSeenAt = DateTimeOffset.UtcNow;

        foreach (var connection in request.Connections ?? [])
        {
            if (string.IsNullOrWhiteSpace(connection.Provider) || string.IsNullOrWhiteSpace(connection.SubscriptionId))
            {
                continue;
            }

            var provider = NormalizeProvider(connection.Provider);
            var subscriptionId = connection.SubscriptionId.Trim();

            var existingConnection = account.Connections.FirstOrDefault(existing =>
                existing.Provider == provider &&
                existing.SubscriptionId == subscriptionId);

            if (existingConnection is null)
            {
                account.Connections.Add(new CloudConnection
                {
                    Provider = provider,
                    SubscriptionId = subscriptionId,
                    DisplayName = connection.DisplayName,
                    AuthType = connection.AuthType,
                    ExternalAccountId = connection.ExternalAccountId,
                    AwsRoleArn = connection.AwsRoleArn,
                    AccessToken = connection.AccessToken,
                    RefreshToken = connection.RefreshToken,
                    TokenExpiresAt = connection.TokenExpiresAt,
                    ConnectedAt = DateTimeOffset.UtcNow,
                    IsActive = true,
                    SyncStatus = "Pending"
                });
            }
            else
            {
                existingConnection.DisplayName = connection.DisplayName ?? existingConnection.DisplayName;
                existingConnection.AuthType = connection.AuthType ?? existingConnection.AuthType;
                existingConnection.ExternalAccountId = connection.ExternalAccountId ?? existingConnection.ExternalAccountId;
                existingConnection.AwsRoleArn = connection.AwsRoleArn ?? existingConnection.AwsRoleArn;
                existingConnection.AccessToken = connection.AccessToken ?? existingConnection.AccessToken;
                existingConnection.RefreshToken = connection.RefreshToken ?? existingConnection.RefreshToken;
                existingConnection.TokenExpiresAt = connection.TokenExpiresAt ?? existingConnection.TokenExpiresAt;
                existingConnection.IsActive = true;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new
        {
            message = "Setup completed.",
            user = ToUserDto(account)
        });
    }

    internal static string NormalizeProvider(string provider) =>
        provider.Trim().ToUpperInvariant() switch
        {
            "AZURE" => "Azure",
            "AWS" => "AWS",
            "GCP" => "GCP",
            _ => provider.Trim()
        };

    internal static object ToUserDto(UserAccount account) => new
    {
        id = account.Id,
        authId = account.AuthId,
        name = account.Name,
        email = account.Email,
        phone = account.Phone,
        imageUrl = account.ImageUrl,
        preferredChannel = account.PreferredChannel,
        smsRecipients = ParseRecipients(account.SmsRecipientsJson, account.Phone),
        whatsAppRecipients = ParseRecipients(account.WhatsAppRecipientsJson),
        isSetupComplete = account.Connections.Any(),
        connections = account.Connections
            .OrderByDescending(connection => connection.LastSyncedAt ?? connection.ConnectedAt)
            .Select(connection => new
            {
                id = connection.Id,
                provider = connection.Provider,
                subscriptionId = connection.SubscriptionId,
                displayName = connection.DisplayName,
                authType = connection.AuthType,
                externalAccountId = connection.ExternalAccountId,
                awsRoleArn = connection.AwsRoleArn,
                isActive = connection.IsActive,
                connectedAt = connection.ConnectedAt,
                lastSyncedAt = connection.LastSyncedAt,
                syncStatus = connection.SyncStatus,
                syncMessage = connection.SyncMessage
            }),
        schedules = account.ReportSchedules
            .Where(schedule => schedule.IsActive)
            .Select(schedule => new
            {
                id = schedule.Id,
                frequency = schedule.Frequency,
                includeCostAnalysis = schedule.IncludeCostAnalysis,
                includeSecurityHealth = schedule.IncludeSecurityHealth,
                lastSentAt = schedule.LastSentAt,
                nextRunAt = schedule.NextRunAt,
                isActive = schedule.IsActive
            })
    };

    private static string? SerializeRecipients(IEnumerable<string> recipients)
    {
        var normalizedRecipients = recipients
            .Select(recipient => recipient.Trim())
            .Where(recipient => !string.IsNullOrWhiteSpace(recipient))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalizedRecipients.Length == 0
            ? null
            : JsonSerializer.Serialize(normalizedRecipients);
    }

    private static string[] ParseRecipients(string? recipientsJson, string? fallbackRecipient = null)
    {
        try
        {
            var parsedRecipients = string.IsNullOrWhiteSpace(recipientsJson)
                ? Array.Empty<string>()
                : JsonSerializer.Deserialize<string[]>(recipientsJson) ?? Array.Empty<string>();

            var normalizedRecipients = parsedRecipients
                .Select(recipient => recipient.Trim())
                .Where(recipient => !string.IsNullOrWhiteSpace(recipient))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (normalizedRecipients.Count == 0 && !string.IsNullOrWhiteSpace(fallbackRecipient))
            {
                normalizedRecipients.Add(fallbackRecipient.Trim());
            }

            return normalizedRecipients.ToArray();
        }
        catch
        {
            return string.IsNullOrWhiteSpace(fallbackRecipient)
                ? Array.Empty<string>()
                : new[] { fallbackRecipient.Trim() };
        }
    }

    private static string GenerateJwtForUser(UserAccount user, IConfiguration configuration)
    {
        var secret = configuration["JWT_SECRET"] ?? "super-secret-dragon-key-2026-draco-sentinel";
        var key = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new("sub", user.AuthId ?? user.Id.ToString())
        };

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            claims.Add(new Claim(ClaimTypes.Email, user.Email));
        }

        if (!string.IsNullOrWhiteSpace(user.Name))
        {
            claims.Add(new Claim(ClaimTypes.Name, user.Name));
        }

        if (!string.IsNullOrWhiteSpace(user.Phone))
        {
            claims.Add(new Claim(ClaimTypes.MobilePhone, user.Phone));
        }

        if (!string.IsNullOrWhiteSpace(user.ImageUrl))
        {
            claims.Add(new Claim("picture", user.ImageUrl));
        }

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public sealed record WorkOsExchangeRequest(string Code, string CodeVerifier);
public sealed record WorkOsUserResponse(
    [property: JsonPropertyName("id")] string id, 
    [property: JsonPropertyName("email")] string email, 
    [property: JsonPropertyName("first_name")] string first_name, 
    [property: JsonPropertyName("last_name")] string last_name, 
    [property: JsonPropertyName("profile_picture_url")] string? profile_picture_url);
public sealed record WorkOsAuthResponse(WorkOsUserResponse user);
public sealed record WorkOsSyncRequest(string WorkOsUserId, string Email, string? Name, string? ImageUrl);

public sealed record SetupCompleteRequest(
    string? Phone,
    string? Name,
    string? PreferredChannel,
    string[]? SmsRecipients,
    string[]? WhatsAppRecipients,
    List<SetupCloudConnectionRequest>? Connections);

public sealed record SetupCloudConnectionRequest(
    string Provider,
    string SubscriptionId,
    string? DisplayName,
    string? AuthType,
    string? ExternalAccountId,
    string? AwsRoleArn,
    string? AccessToken,
    string? RefreshToken,
    DateTimeOffset? TokenExpiresAt);
