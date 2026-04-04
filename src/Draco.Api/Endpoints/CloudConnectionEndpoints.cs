using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Amazon;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using Azure.Core;
using Azure.ResourceManager;
using Draco.Application.Interfaces;
using Draco.Domain.Entities;
using Draco.Domain.Repositories;
using Draco.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Draco.Api.Endpoints;

public static class CloudConnectionEndpoints
{
    private const string AzureManagementScope = "https://management.azure.com/user_impersonation offline_access openid profile email";

    public static void MapCloudConnectionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/cloud-connections").RequireAuthorization();

        group.MapGet("/", ListConnectionsAsync)
            .WithName("ListCloudConnections");

        group.MapGet("/azure/authorize-url", GetAzureAuthorizeUrlAsync)
            .WithName("GetAzureAuthorizeUrl");

        group.MapPost("/azure/exchange", ExchangeAzureCodeAsync)
            .WithName("ExchangeAzureCode");

        group.MapGet("/aws/bootstrap", GetAwsBootstrapAsync)
            .WithName("GetAwsBootstrap");

        group.MapPost("/", UpsertConnectionAsync)
            .WithName("UpsertCloudConnection");

        group.MapDelete("/{id:int}", DeleteConnectionAsync)
            .WithName("DeleteCloudConnection");

        group.MapGet("/{id:int}/eventing-export", GetEventingExportAsync)
            .WithName("GetCloudConnectionEventingExport");

        group.MapPost("/sync", SyncConnectionsAsync)
            .WithName("SyncCloudConnections");
    }

    private static async Task<IResult> ListConnectionsAsync(
        ClaimsPrincipal userPrincipal,
        DracoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var user = await userPrincipal.GetCurrentUserAsync(dbContext, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(user.Connections
            .OrderByDescending(connection => connection.LastSyncedAt ?? connection.ConnectedAt)
            .Select(ToConnectionDto));
    }

    private static IResult GetAzureAuthorizeUrlAsync(
        [FromQuery] string? redirectUri,
        [FromQuery] string? state,
        IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(redirectUri) || !Uri.TryCreate(redirectUri, UriKind.Absolute, out _))
        {
            return Results.BadRequest(new { message = "A valid redirect URI is required." });
        }

        if (string.IsNullOrWhiteSpace(state))
        {
            return Results.BadRequest(new { message = "OAuth state is required." });
        }

        var clientId = configuration["AZURE_CLIENT_ID"];
        var tenantId = GetAzureTenantSegment(configuration);

        if (string.IsNullOrWhiteSpace(clientId))
        {
            return Results.Problem("Azure OAuth is not configured on the API.", statusCode: StatusCodes.Status500InternalServerError);
        }

        var authorizeUrl = new UriBuilder($"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize");
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = clientId,
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri,
            ["response_mode"] = "query",
            ["scope"] = AzureManagementScope,
            ["state"] = state,
            ["prompt"] = "select_account"
        };

        authorizeUrl.Query = string.Join("&", query.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value ?? string.Empty)}"));

        return Results.Ok(new { authorizeUrl = authorizeUrl.ToString() });
    }

    private static async Task<IResult> ExchangeAzureCodeAsync(
        [FromBody] AzureCodeExchangeRequest request,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.RedirectUri))
        {
            return Results.BadRequest(new { message = "Authorization code and redirect URI are required." });
        }

        if (!Uri.TryCreate(request.RedirectUri, UriKind.Absolute, out _))
        {
            return Results.BadRequest(new { message = "Redirect URI must be absolute." });
        }

        var logger = loggerFactory.CreateLogger("AzureOAuth");

        try
        {
            using var client = httpClientFactory.CreateClient();
            var tokenResponse = await ExchangeAzureTokenAsync(
                client,
                configuration,
                logger,
                new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = request.Code.Trim(),
                    ["redirect_uri"] = request.RedirectUri.Trim(),
                    ["scope"] = AzureManagementScope
                },
                cancellationToken);

            var subscriptions = await ListAzureSubscriptionsAsync(tokenResponse.access_token, cancellationToken);

            return Results.Ok(new AzureExchangeResponse(
                tokenResponse.access_token,
                tokenResponse.refresh_token,
                DateTimeOffset.UtcNow.AddSeconds(tokenResponse.expires_in),
                subscriptions));
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Azure OAuth exchange configuration is invalid.");
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Azure OAuth exchange failed.");
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static async Task<IResult> UpsertConnectionAsync(
        [FromBody] CloudConnectionRequest request,
        ClaimsPrincipal userPrincipal,
        DracoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var user = await userPrincipal.GetCurrentUserAsync(dbContext, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Provider) || string.IsNullOrWhiteSpace(request.SubscriptionId))
        {
            return Results.BadRequest(new { message = "Provider and subscription ID are required." });
        }

        var provider = AuthEndpoints.NormalizeProvider(request.Provider);
        var subscriptionId = request.SubscriptionId.Trim();
        var authType = ResolveConnectionAuthType(provider, request.AuthType, request.AccessToken);

        if (string.Equals(provider, "AWS", StringComparison.OrdinalIgnoreCase))
        {
            var validationMessage = ValidateAwsConnectionRequest(request, authType);
            if (!string.IsNullOrWhiteSpace(validationMessage))
            {
                return Results.BadRequest(new { message = validationMessage });
            }
        }

        var connection = user.Connections.FirstOrDefault(existing =>
            existing.Provider == provider &&
            existing.SubscriptionId == subscriptionId);

        if (connection is null)
        {
            connection = new CloudConnection
            {
                UserId = user.Id,
                Provider = provider,
                SubscriptionId = subscriptionId,
                DisplayName = request.DisplayName,
                AuthType = authType,
                ExternalAccountId = request.ExternalAccountId,
                AwsRoleArn = GetStoredAwsRoleArn(provider, authType, request.AwsRoleArn),
                AccessToken = GetStoredAccessToken(provider, authType, request.AccessToken),
                RefreshToken = GetStoredRefreshToken(provider, request.RefreshToken),
                TokenExpiresAt = GetStoredTokenExpiresAt(provider, request.TokenExpiresAt),
                ConnectedAt = DateTimeOffset.UtcNow,
                IsActive = true,
                SyncStatus = "Pending"
            };

            dbContext.CloudConnections.Add(connection);
        }
        else
        {
            connection.DisplayName = request.DisplayName ?? connection.DisplayName;
            connection.AuthType = authType ?? connection.AuthType;
            connection.ExternalAccountId = request.ExternalAccountId ?? connection.ExternalAccountId;
            connection.AwsRoleArn = GetUpdatedAwsRoleArn(provider, authType, request.AwsRoleArn, connection.AwsRoleArn);
            connection.AccessToken = GetUpdatedAccessToken(provider, authType, request.AccessToken, connection.AccessToken);
            connection.RefreshToken = GetUpdatedRefreshToken(provider, request.RefreshToken, connection.RefreshToken);
            connection.TokenExpiresAt = GetUpdatedTokenExpiresAt(provider, request.TokenExpiresAt, connection.TokenExpiresAt);
            connection.IsActive = request.IsActive ?? connection.IsActive;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToConnectionDto(connection));
    }

    private static async Task<IResult> GetAwsBootstrapAsync(
        [FromQuery] string? accountId,
        [FromQuery] string? roleName,
        ClaimsPrincipal userPrincipal,
        DracoDbContext dbContext,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var user = await userPrincipal.GetCurrentUserAsync(dbContext, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(accountId) || accountId.Length != 12 || !accountId.All(char.IsDigit))
        {
            return Results.BadRequest(new { message = "A valid 12-digit AWS account ID is required." });
        }

        var trustedPrincipalArn = configuration["AWS_ASSUME_ROLE_PRINCIPAL_ARN"];
        if (string.IsNullOrWhiteSpace(trustedPrincipalArn))
        {
            trustedPrincipalArn = await ResolveAwsTrustedPrincipalArnAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(trustedPrincipalArn))
        {
            return Results.Problem(
                "Draco could not determine the AWS principal to trust for cross-account role access. Configure AWS runtime credentials or set AWS_ASSUME_ROLE_PRINCIPAL_ARN on the API.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        var suggestedRoleName = SanitizeAwsRoleName(roleName, user.Id);
        var externalId = BuildAwsExternalId(user.Id, accountId);
        var suggestedRoleArn = $"arn:aws:iam::{accountId}:role/{suggestedRoleName}";

        return Results.Ok(new AwsBootstrapResponse(
            AccountId: accountId,
            TrustedPrincipalArn: trustedPrincipalArn,
            ExternalId: externalId,
            SuggestedRoleName: suggestedRoleName,
            SuggestedRoleArn: suggestedRoleArn,
            TrustPolicyJson: BuildAwsTrustPolicyJson(trustedPrincipalArn, externalId),
            PermissionsPolicyJson: BuildAwsPermissionsPolicyJson(),
            TerraformTemplate: BuildAwsTerraformTemplate(trustedPrincipalArn, externalId, suggestedRoleName)));
    }

    private static async Task<IResult> DeleteConnectionAsync(
        int id,
        ClaimsPrincipal userPrincipal,
        DracoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var user = await userPrincipal.GetCurrentUserAsync(dbContext, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var connection = user.Connections.FirstOrDefault(existing => existing.Id == id);
        if (connection is null)
        {
            return Results.NotFound(new { message = "Cloud connection not found." });
        }

        connection.IsActive = false;
        connection.SyncStatus = "Disconnected";
        connection.SyncMessage = "Connection disabled by user.";

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { message = "Cloud connection disabled." });
    }

    private static async Task<IResult> GetEventingExportAsync(
        int id,
        ClaimsPrincipal userPrincipal,
        DracoDbContext dbContext,
        HttpContext httpContext,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var user = await userPrincipal.GetCurrentUserAsync(dbContext, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var connection = user.Connections.FirstOrDefault(existing => existing.Id == id && existing.IsActive);
        if (connection is null)
        {
            return Results.NotFound(new { message = "Active cloud connection not found." });
        }

        var ingestionSecret = configuration["DRACO_EVENT_INGESTION_SECRET"];
        if (string.IsNullOrWhiteSpace(ingestionSecret))
        {
            return Results.Problem(
                "DRACO_EVENT_INGESTION_SECRET is not configured on the API.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        var provider = AuthEndpoints.NormalizeProvider(connection.Provider);
        var userEmail = user.Email?.Trim() ?? string.Empty;
        var baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{httpContext.Request.PathBase}".TrimEnd('/');
        var detectedLocations = await dbContext.CloudResources
            .Where(resource =>
                resource.Provider == provider &&
                resource.SubscriptionId == connection.SubscriptionId &&
                !string.IsNullOrWhiteSpace(resource.Location))
            .Select(resource => resource.Location.Trim())
            .Distinct()
            .OrderBy(location => location)
            .ToListAsync(cancellationToken);

        var defaultLocation = detectedLocations.FirstOrDefault() ??
            (string.Equals(provider, "Azure", StringComparison.OrdinalIgnoreCase) ? "eastus" : "us-east-1");

        if (string.Equals(provider, "Azure", StringComparison.OrdinalIgnoreCase))
        {
            var webhookBaseUrl = $"{baseUrl}/api/events/azure/activity-log";
            var detectedResourceGroups = await dbContext.CloudResources
                .Where(resource =>
                    resource.Provider == provider &&
                    resource.SubscriptionId == connection.SubscriptionId &&
                    !string.IsNullOrWhiteSpace(resource.ResourceGroupName))
                .Select(resource => resource.ResourceGroupName.Trim())
                .Distinct()
                .OrderBy(resourceGroup => resourceGroup)
                .ToListAsync(cancellationToken);
            var defaultResourceGroup = detectedResourceGroups.FirstOrDefault() ?? "draco-monitoring-rg";
            var discoveryReady = detectedLocations.Count > 0 && detectedResourceGroups.Count > 0;

            return Results.Ok(new CloudConnectionEventingExportResponse(
                Provider: provider,
                ConnectionId: connection.Id,
                DisplayName: connection.DisplayName,
                SubscriptionId: connection.SubscriptionId,
                WebhookUrl: BuildAzureWebhookUrl(webhookBaseUrl, ingestionSecret, userEmail),
                Variables: new Dictionary<string, string>
                {
                    ["resource_group_name"] = defaultResourceGroup,
                    ["location"] = defaultLocation,
                    ["subscription_id"] = connection.SubscriptionId,
                    ["draco_activity_webhook_url"] = webhookBaseUrl,
                    ["draco_event_ingestion_secret"] = ingestionSecret,
                    ["draco_user_email"] = userEmail
                },
                TfvarsText: BuildTfvarsText(new (string Key, string Value)[]
                {
                    ("resource_group_name", defaultResourceGroup),
                    ("location", defaultLocation),
                    ("subscription_id", connection.SubscriptionId),
                    ("draco_activity_webhook_url", webhookBaseUrl),
                    ("draco_event_ingestion_secret", ingestionSecret),
                    ("draco_user_email", userEmail)
                }),
                TemplatePath: "src/Draco.Cli/Infrastructure/Terraform/examples/azure-activity-log-alert.tf.example",
                DetectedLocations: detectedLocations,
                DefaultLocation: defaultLocation,
                DetectedResourceGroups: detectedResourceGroups,
                DefaultResourceGroup: defaultResourceGroup,
                DiscoveryReady: discoveryReady,
                DiscoveryMessage: discoveryReady
                    ? "Detected resource groups and locations are ready for this Azure connection."
                    : "Draco needs a completed sync for this Azure connection before resource group and location selectors are fully available."));
        }

        var eventsIngestUrl = $"{baseUrl}/api/events/ingest";
        var awsDiscoveryReady = detectedLocations.Count > 0;
        return Results.Ok(new CloudConnectionEventingExportResponse(
            Provider: provider,
            ConnectionId: connection.Id,
            DisplayName: connection.DisplayName,
            SubscriptionId: connection.SubscriptionId,
            WebhookUrl: eventsIngestUrl,
            Variables: new Dictionary<string, string>
            {
                ["aws_region"] = defaultLocation,
                ["draco_api_events_ingest_url"] = eventsIngestUrl,
                ["draco_event_ingestion_secret"] = ingestionSecret,
                ["draco_user_email"] = userEmail
            },
            TfvarsText: BuildTfvarsText(new (string Key, string Value)[]
            {
                ("aws_region", defaultLocation),
                ("draco_api_events_ingest_url", eventsIngestUrl),
                ("draco_event_ingestion_secret", ingestionSecret),
                ("draco_user_email", userEmail)
            }),
            TemplatePath: "src/Draco.Cli/Infrastructure/Terraform/examples/aws-eventbridge-forwarder.tf.example",
            DetectedLocations: detectedLocations,
            DefaultLocation: defaultLocation,
            DetectedResourceGroups: new List<string>(),
            DefaultResourceGroup: null,
            DiscoveryReady: awsDiscoveryReady,
            DiscoveryMessage: awsDiscoveryReady
                ? "Detected AWS regions are ready for this connection."
                : "Draco needs a completed sync for this AWS connection before region selection is fully available."));
    }

    private static async Task<IResult> SyncConnectionsAsync(
        [FromBody] SyncConnectionsRequest? request,
        ClaimsPrincipal userPrincipal,
        DracoDbContext dbContext,
        ICloudConnectionSyncService cloudConnectionSyncService,
        CancellationToken cancellationToken)
    {
        var user = await userPrincipal.GetCurrentUserAsync(dbContext, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }
        var syncResult = await cloudConnectionSyncService.SyncUserConnectionsAsync(user, request?.ConnectionIds, cancellationToken);

        return Results.Ok(new
        {
            connections = syncResult.Connections,
            results = syncResult.Results,
            notifications = syncResult.Notifications
        });
    }

    private static object ToConnectionDto(CloudConnection connection) => new
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
    };

    private static string BuildAzureWebhookUrl(string webhookBaseUrl, string ingestionSecret, string userEmail)
    {
        var query = new List<string>
        {
            $"code={Uri.EscapeDataString(ingestionSecret)}"
        };

        if (!string.IsNullOrWhiteSpace(userEmail))
        {
            query.Add($"userEmail={Uri.EscapeDataString(userEmail)}");
        }

        return $"{webhookBaseUrl}?{string.Join("&", query)}";
    }

    private static string BuildTfvarsText(IEnumerable<(string Key, string Value)> values) =>
        string.Join(Environment.NewLine, values.Select(entry =>
            $"{entry.Key} = {JsonSerializer.Serialize(entry.Value)}"));

    private static string GetAzureTenantSegment(IConfiguration configuration) =>
        string.IsNullOrWhiteSpace(configuration["AZURE_TENANT_ID"])
            ? "organizations"
            : configuration["AZURE_TENANT_ID"]!.Trim();

    private static string? ValidateAwsConnectionRequest(CloudConnectionRequest request, string? authType)
    {
        if (string.Equals(authType, "AwsAssumeRole", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(request.AwsRoleArn)
                ? "AWS assume-role connections require the role ARN you created in your AWS account."
                : null;
        }

        if (!string.Equals(authType, "AwsStaticCredentials", StringComparison.OrdinalIgnoreCase))
        {
            return "Choose either Assume Role or Access Keys before connecting this AWS account.";
        }

        if (string.IsNullOrWhiteSpace(request.AccessToken))
        {
            return "AWS access-key connections require both an access key ID and secret access key.";
        }

        try
        {
            using var document = JsonDocument.Parse(request.AccessToken);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return "AWS connection payload must be a JSON object.";
            }

            var accessKeyId = root.TryGetProperty("accessKeyId", out var accessKeyIdValue)
                ? accessKeyIdValue.GetString()
                : null;
            var secretAccessKey = root.TryGetProperty("secretAccessKey", out var secretAccessKeyValue)
                ? secretAccessKeyValue.GetString()
                : null;

            return string.IsNullOrWhiteSpace(accessKeyId) || string.IsNullOrWhiteSpace(secretAccessKey)
                ? "AWS access-key connections require both an access key ID and secret access key."
                : null;
        }
        catch (JsonException)
        {
            return "AWS connection payload must be valid JSON generated by the Draco setup flow.";
        }
    }

    private static string? ResolveConnectionAuthType(string provider, string? requestedAuthType, string? accessToken)
    {
        if (string.Equals(provider, "Azure", StringComparison.OrdinalIgnoreCase))
        {
            return "AzureOAuth";
        }

        if (!string.Equals(provider, "AWS", StringComparison.OrdinalIgnoreCase))
        {
            return requestedAuthType;
        }

        if (!string.IsNullOrWhiteSpace(requestedAuthType))
        {
            return requestedAuthType.Trim();
        }

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(accessToken);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return root.TryGetProperty("kind", out var kindValue)
                ? kindValue.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetStoredAwsRoleArn(string provider, string? authType, string? awsRoleArn) =>
        string.Equals(provider, "AWS", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(authType, "AwsAssumeRole", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(awsRoleArn)
            ? awsRoleArn.Trim()
            : null;

    private static string? GetUpdatedAwsRoleArn(string provider, string? authType, string? requestedAwsRoleArn, string? existingAwsRoleArn)
    {
        if (!string.Equals(provider, "AWS", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.Equals(authType, "AwsAssumeRole", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(requestedAwsRoleArn)
                ? existingAwsRoleArn
                : requestedAwsRoleArn.Trim();
        }

        return null;
    }

    private static string? GetStoredAccessToken(string provider, string? authType, string? accessToken) =>
        string.Equals(provider, "AWS", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(authType, "AwsAssumeRole", StringComparison.OrdinalIgnoreCase)
            ? null
            : accessToken;

    private static string? GetUpdatedAccessToken(string provider, string? authType, string? requestedAccessToken, string? existingAccessToken)
    {
        if (string.Equals(provider, "AWS", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(authType, "AwsAssumeRole", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return requestedAccessToken ?? existingAccessToken;
    }

    private static string? GetStoredRefreshToken(string provider, string? refreshToken) =>
        string.Equals(provider, "Azure", StringComparison.OrdinalIgnoreCase)
            ? refreshToken
            : null;

    private static string? GetUpdatedRefreshToken(string provider, string? requestedRefreshToken, string? existingRefreshToken) =>
        string.Equals(provider, "Azure", StringComparison.OrdinalIgnoreCase)
            ? requestedRefreshToken ?? existingRefreshToken
            : null;

    private static DateTimeOffset? GetStoredTokenExpiresAt(string provider, DateTimeOffset? tokenExpiresAt) =>
        string.Equals(provider, "Azure", StringComparison.OrdinalIgnoreCase)
            ? tokenExpiresAt
            : null;

    private static DateTimeOffset? GetUpdatedTokenExpiresAt(string provider, DateTimeOffset? requestedTokenExpiresAt, DateTimeOffset? existingTokenExpiresAt) =>
        string.Equals(provider, "Azure", StringComparison.OrdinalIgnoreCase)
            ? requestedTokenExpiresAt ?? existingTokenExpiresAt
            : null;

    private static async Task<string?> ResolveProviderAccessTokenAsync(
        CloudConnection connection,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (string.Equals(connection.Provider, "Azure", StringComparison.OrdinalIgnoreCase))
        {
            return await EnsureAzureAccessTokenAsync(
                connection,
                configuration,
                httpClientFactory,
                logger,
                cancellationToken);
        }

        if (string.Equals(connection.Provider, "AWS", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(connection.AuthType, "AwsAssumeRole", StringComparison.OrdinalIgnoreCase))
        {
            var accountId = string.IsNullOrWhiteSpace(connection.ExternalAccountId)
                ? connection.SubscriptionId
                : connection.ExternalAccountId.Trim();

            if (string.IsNullOrWhiteSpace(connection.AwsRoleArn))
            {
                throw new InvalidOperationException("AWS assume-role connection is missing the role ARN. Reconnect this AWS account.");
            }

            return JsonSerializer.Serialize(new
            {
                kind = "AwsAssumeRole",
                roleArn = connection.AwsRoleArn.Trim(),
                externalId = BuildAwsExternalId(connection.UserId, accountId)
            });
        }

        return connection.AccessToken;
    }

    private static async Task<string?> EnsureAzureAccessTokenAsync(
        CloudConnection connection,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var expiresSoon = connection.TokenExpiresAt.HasValue &&
            connection.TokenExpiresAt.Value <= DateTimeOffset.UtcNow.AddMinutes(5);

        if (!string.IsNullOrWhiteSpace(connection.AccessToken) && !expiresSoon)
        {
            return connection.AccessToken;
        }

        if (string.IsNullOrWhiteSpace(connection.RefreshToken))
        {
            if (!string.IsNullOrWhiteSpace(connection.AccessToken))
            {
                return connection.AccessToken;
            }

            throw new InvalidOperationException("Azure connection requires re-authentication.");
        }

        using var client = httpClientFactory.CreateClient();
        var tokenResponse = await ExchangeAzureTokenAsync(
            client,
            configuration,
            logger,
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = connection.RefreshToken,
                ["scope"] = AzureManagementScope
            },
            cancellationToken);

        connection.AccessToken = tokenResponse.access_token;
        connection.RefreshToken = string.IsNullOrWhiteSpace(tokenResponse.refresh_token)
            ? connection.RefreshToken
            : tokenResponse.refresh_token;
        connection.TokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.expires_in);

        return connection.AccessToken;
    }

    private static async Task<AzureTokenResponse> ExchangeAzureTokenAsync(
        HttpClient client,
        IConfiguration configuration,
        ILogger logger,
        IReadOnlyDictionary<string, string> grantValues,
        CancellationToken cancellationToken)
    {
        var clientId = configuration["AZURE_CLIENT_ID"];
        var clientSecret = configuration["AZURE_CLIENT_SECRET"];
        var tenantId = GetAzureTenantSegment(configuration);

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException("Azure OAuth is not configured. AZURE_CLIENT_ID and AZURE_CLIENT_SECRET are required.");
        }

        var payload = new Dictionary<string, string>(grantValues)
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token")
        {
            Content = new FormUrlEncodedContent(payload)
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("Azure OAuth exchange failed with status {StatusCode}: {ErrorContent}", (int)response.StatusCode, errorContent);
            throw new HttpRequestException("Microsoft sign-in could not be completed.");
        }

        var tokenResponse = await response.Content.ReadFromJsonAsync<AzureTokenResponse>(cancellationToken: cancellationToken);
        if (tokenResponse is null || string.IsNullOrWhiteSpace(tokenResponse.access_token))
        {
            throw new HttpRequestException("Microsoft sign-in returned an invalid token response.");
        }

        return tokenResponse;
    }

    private static async Task<List<AzureSubscriptionOption>> ListAzureSubscriptionsAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        var client = new ArmClient(new AzureBearerTokenCredential(accessToken));
        var subscriptions = new List<AzureSubscriptionOption>();

        await foreach (var subscription in client.GetSubscriptions().GetAllAsync(cancellationToken))
        {
            subscriptions.Add(new AzureSubscriptionOption(
                subscription.Data.SubscriptionId ?? string.Empty,
                string.IsNullOrWhiteSpace(subscription.Data.DisplayName)
                    ? subscription.Data.SubscriptionId ?? "Unnamed subscription"
                    : subscription.Data.DisplayName,
                subscription.Data.State.ToString()));
        }

        return subscriptions
            .Where(subscription => !string.IsNullOrWhiteSpace(subscription.SubscriptionId))
            .OrderBy(subscription => subscription.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ExtractResourceGroupName(string? resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return string.Empty;
        }

        var segments = resourceId.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (segments[index].Equals("resourceGroups", StringComparison.OrdinalIgnoreCase))
            {
                return segments[index + 1];
            }
        }

        return string.Empty;
    }

    private static async Task<string?> ResolveAwsTrustedPrincipalArnAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var stsClient = new AmazonSecurityTokenServiceClient(RegionEndpoint.USEast1);
            var identity = await stsClient.GetCallerIdentityAsync(new GetCallerIdentityRequest(), cancellationToken);
            return NormalizeAwsTrustedPrincipalArn(identity.Arn, identity.Account);
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeAwsTrustedPrincipalArn(string? arn, string? accountId)
    {
        if (string.IsNullOrWhiteSpace(arn))
        {
            return string.Empty;
        }

        const string assumedRoleMarker = ":assumed-role/";
        var markerIndex = arn.IndexOf(assumedRoleMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return arn.Trim();
        }

        var roleSegment = arn[(markerIndex + assumedRoleMarker.Length)..];
        var slashIndex = roleSegment.IndexOf('/');
        var roleName = slashIndex >= 0 ? roleSegment[..slashIndex] : roleSegment;
        if (string.IsNullOrWhiteSpace(roleName) || string.IsNullOrWhiteSpace(accountId))
        {
            return arn.Trim();
        }

        return $"arn:aws:iam::{accountId}:role/{roleName}";
    }

    private static string SanitizeAwsRoleName(string? requestedRoleName, Guid userId)
    {
        var candidate = string.IsNullOrWhiteSpace(requestedRoleName)
            ? $"draco-readonly-{userId:N}"[..23]
            : requestedRoleName.Trim();

        var builder = new StringBuilder(candidate.Length);
        foreach (var character in candidate)
        {
            if (char.IsLetterOrDigit(character) || character is '+' or '=' or ',' or '.' or '@' or '_' or '-')
            {
                builder.Append(character);
            }
        }

        var sanitized = builder.ToString().Trim('-');
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = $"draco-readonly-{userId:N}"[..23];
        }

        return sanitized.Length <= 64 ? sanitized : sanitized[..64];
    }

    private static string BuildAwsExternalId(Guid userId, string accountId) =>
        $"draco-{accountId}-{userId:N}";

    private static string BuildAwsTrustPolicyJson(string trustedPrincipalArn, string externalId) =>
        $$"""
        {
          "Version": "2012-10-17",
          "Statement": [
            {
              "Effect": "Allow",
              "Principal": {
                "AWS": "{{trustedPrincipalArn}}"
              },
              "Action": "sts:AssumeRole",
              "Condition": {
                "StringEquals": {
                  "sts:ExternalId": "{{externalId}}"
                }
              }
            }
          ]
        }
        """;

    private static string BuildAwsPermissionsPolicyJson() =>
        """
        {
          "Version": "2012-10-17",
          "Statement": [
            {
              "Effect": "Allow",
              "Action": [
                "budgets:Describe*",
                "ce:GetCostAndUsage",
                "ce:GetCostAndUsageWithResources",
                "ce:GetCostForecast",
                "ce:GetDimensionValues",
                "cloudwatch:GetMetricData",
                "cloudwatch:ListMetrics",
                "ec2:Describe*",
                "s3:GetBucketLocation",
                "s3:ListAllMyBuckets",
                "sts:GetCallerIdentity"
              ],
              "Resource": "*"
            }
          ]
        }
        """;

    private static string BuildAwsTerraformTemplate(
        string trustedPrincipalArn,
        string externalId,
        string roleName) =>
        $$"""
        terraform {
          required_version = ">= 1.5.0"

          required_providers {
            aws = {
              source  = "hashicorp/aws"
              version = "~> 5.0"
            }
          }
        }

        data "aws_caller_identity" "current" {}

        locals {
          draco_role_name = "{{roleName}}"
          draco_external_id = "{{externalId}}"
          draco_trusted_principal_arn = "{{trustedPrincipalArn}}"
        }

        resource "aws_iam_role" "draco_readonly" {
          name = local.draco_role_name

          assume_role_policy = jsonencode({
            Version = "2012-10-17"
            Statement = [
              {
                Effect = "Allow"
                Principal = {
                  AWS = local.draco_trusted_principal_arn
                }
                Action = "sts:AssumeRole"
                Condition = {
                  StringEquals = {
                    "sts:ExternalId" = local.draco_external_id
                  }
                }
              }
            ]
          })
        }

        resource "aws_iam_role_policy" "draco_readonly" {
          name = "draco-readonly-access"
          role = aws_iam_role.draco_readonly.id

          policy = jsonencode({
            Version = "2012-10-17"
            Statement = [
              {
                Effect = "Allow"
                Action = [
                  "budgets:Describe*",
                  "ce:GetCostAndUsage",
                  "ce:GetCostAndUsageWithResources",
                  "ce:GetCostForecast",
                  "ce:GetDimensionValues",
                  "cloudwatch:GetMetricData",
                  "cloudwatch:ListMetrics",
                  "ec2:Describe*",
                  "s3:GetBucketLocation",
                  "s3:ListAllMyBuckets",
                  "sts:GetCallerIdentity"
                ]
                Resource = "*"
              }
            ]
          })
        }

        output "draco_role_arn" {
          value = aws_iam_role.draco_readonly.arn
        }

        output "aws_account_id" {
          value = data.aws_caller_identity.current.account_id
        }

        output "draco_external_id" {
          value = local.draco_external_id
        }
        """;
}

public sealed record CloudConnectionRequest(
    string Provider,
    string SubscriptionId,
    string? DisplayName,
    string? AuthType,
    string? ExternalAccountId,
    string? AwsRoleArn,
    string? AccessToken,
    string? RefreshToken,
    DateTimeOffset? TokenExpiresAt,
    bool? IsActive);

public sealed record SyncConnectionsRequest(List<int>? ConnectionIds);
public sealed record CloudConnectionEventingExportResponse(
    string Provider,
    int ConnectionId,
    string? DisplayName,
    string SubscriptionId,
    string WebhookUrl,
    Dictionary<string, string> Variables,
    string TfvarsText,
    string TemplatePath,
    List<string> DetectedLocations,
    string DefaultLocation,
    List<string> DetectedResourceGroups,
    string? DefaultResourceGroup,
    bool DiscoveryReady,
    string DiscoveryMessage);
public sealed record AzureCodeExchangeRequest(string Code, string RedirectUri);
public sealed record AzureExchangeResponse(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset TokenExpiresAt,
    List<AzureSubscriptionOption> Subscriptions);
public sealed record AzureSubscriptionOption(string SubscriptionId, string DisplayName, string? State);
public sealed record AwsBootstrapResponse(
    string AccountId,
    string TrustedPrincipalArn,
    string ExternalId,
    string SuggestedRoleName,
    string SuggestedRoleArn,
    string TrustPolicyJson,
    string PermissionsPolicyJson,
    string TerraformTemplate);

public sealed class AzureTokenResponse
{
    public string access_token { get; set; } = string.Empty;
    public string? refresh_token { get; set; }
    public int expires_in { get; set; }
}

internal sealed class AzureBearerTokenCredential(string accessToken) : TokenCredential
{
    private readonly AccessToken _token = new(accessToken, DateTimeOffset.UtcNow.AddHours(1));

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => _token;

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_token);
}
