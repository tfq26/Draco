using Draco.Application.Interfaces;
using Draco.Application.Services;
using Draco.Domain.Repositories;
using Draco.Domain.Entities;
using Draco.Infrastructure.Data;
using Draco.Infrastructure.Providers;
using Draco.Infrastructure.Repositories;
using Draco.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using dotenv.net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;

DotEnv.Load();

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Load Configuration
var configuration = builder.Configuration;
var connectionString = configuration["DRACO_DB_MAIN_CONNECTION"] 
                       ?? configuration.GetConnectionString("DracoDbContext") 
                       ?? configuration["DRACO_DB_CONNECTION"] 
                       ?? "Host=localhost;Database=draco;Username=postgres;Password=postgres";

// Database
builder.Services.AddDbContext<DracoDbContext>(options =>
    options.UseNpgsql(connectionString, x => x.UseVector()));

// Add Services
builder.Services.AddHttpClient<IAIService, GeminiAIService>();
builder.Services.AddScoped<IResourceRepository, ResourceRepository>();
builder.Services.AddScoped<ICloudProvider, AzureProvider>();
builder.Services.AddScoped<ICloudProvider, AWSProvider>();
builder.Services.AddScoped<IMessagingService, TwilioService>();
builder.Services.AddScoped<IEmailService, SendGridService>();
builder.Services.AddScoped<IGitProvider, GitHubProvider>();
builder.Services.AddScoped<AlertOrchestrator>();
builder.Services.AddScoped<ResourceDiscoveryService>();
builder.Services.AddScoped<RemediationService>();
builder.Services.AddScoped<PulseReportService>();
builder.Services.AddScoped<ICostGovernanceService, CostGovernanceService>();
builder.Services.AddScoped<ITelemetryService, TelemetryService>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();

// Configure JWT Authentication
// Better Auth uses session cookies, but we also support Bearer tokens for API calls
var neonAuthUrl = builder.Configuration["NEON_AUTH_URL"] ?? "";
var jwtSecret = builder.Configuration["JWT_SECRET"] ?? "super-secret-dragon-key-2026-draco-sentinel";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var key = System.Text.Encoding.ASCII.GetBytes(jwtSecret);
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key)
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                // Accept tokens from query string for OAuth connector flows
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && 
                    (path.StartsWithSegments("/api/auth/azure") || path.StartsWithSegments("/api/auth/aws")))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddHostedService<PulseBackgroundScheduler>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAll");
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

// Database Initialization
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<DracoDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try 
    {
        logger.LogInformation("Verifying database schema...");
        // EnsureCreated works if the DB is empty. 
        // For existing DBs, we'll manually ensure the new tables exist.
        context.Database.EnsureCreated();
        
        // Manual schema sync for new entities since we are avoiding complex migrations in this demo
        context.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""UserAccounts"" (
                ""Phone"" TEXT PRIMARY KEY,
                ""Name"" TEXT NOT NULL,
                ""Email"" TEXT,
                ""ImageUrl"" TEXT,
                ""PreferredChannel"" TEXT,
                ""AuthId"" TEXT,
                ""PasswordHash"" TEXT,
                ""CreatedAt"" TIMESTAMPTZ NOT NULL,
                ""LastSeenAt"" TIMESTAMPTZ NOT NULL
            );
            DO $$ 
            BEGIN 
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='UserAccounts' AND column_name='AuthId') THEN
                    ALTER TABLE ""UserAccounts"" ADD COLUMN ""AuthId"" TEXT;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='UserAccounts' AND column_name='Email') THEN
                    ALTER TABLE ""UserAccounts"" ADD COLUMN ""Email"" TEXT;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='UserAccounts' AND column_name='ImageUrl') THEN
                    ALTER TABLE ""UserAccounts"" ADD COLUMN ""ImageUrl"" TEXT;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='UserAccounts' AND column_name='PreferredChannel') THEN
                    ALTER TABLE ""UserAccounts"" ADD COLUMN ""PreferredChannel"" TEXT;
                END IF;

                UPDATE ""UserAccounts"" SET ""PreferredChannel"" = 'SMS' WHERE ""PreferredChannel"" IS NULL;
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='UserAccounts' AND column_name='PasswordHash') THEN
                    ALTER TABLE ""UserAccounts"" ADD COLUMN ""PasswordHash"" TEXT;
                END IF;
            END $$;
            CREATE TABLE IF NOT EXISTS ""CloudConnections"" (
                ""Id"" SERIAL PRIMARY KEY,
                ""UserPhone"" TEXT NOT NULL,
                ""Provider"" TEXT NOT NULL,
                ""SubscriptionId"" TEXT NOT NULL,
                ""AccessToken"" TEXT,
                ""RefreshToken"" TEXT,
                ""TokenExpiresAt"" TIMESTAMPTZ,
                ""IsActive"" BOOLEAN NOT NULL,
                ""ConnectedAt"" TIMESTAMPTZ NOT NULL
            );
            CREATE TABLE IF NOT EXISTS ""PulseReportSchedules"" (
                ""Id"" SERIAL PRIMARY KEY,
                ""UserPhone"" TEXT NOT NULL REFERENCES ""UserAccounts""(""Phone""),
                ""Frequency"" TEXT NOT NULL,
                ""IncludeCostAnalysis"" BOOLEAN NOT NULL,
                ""IncludeSecurityHealth"" BOOLEAN NOT NULL,
                ""LastSentAt"" TIMESTAMPTZ,
                ""NextRunAt"" TIMESTAMPTZ NOT NULL,
                ""IsActive"" BOOLEAN NOT NULL
            );
            CREATE TABLE IF NOT EXISTS ""CostBudgets"" (
                ""Id"" UUID PRIMARY KEY,
                ""Name"" TEXT NOT NULL,
                ""Provider"" TEXT NOT NULL,
                ""SubscriptionId"" TEXT NOT NULL,
                ""Amount"" DECIMAL NOT NULL,
                ""Currency"" TEXT NOT NULL,
                ""TimeGrain"" TEXT NOT NULL,
                ""AlertThresholdPercentage"" DOUBLE PRECISION NOT NULL,
                ""CreatedAt"" TIMESTAMPTZ NOT NULL,
                ""IsActive"" BOOLEAN NOT NULL
            );
            CREATE TABLE IF NOT EXISTS ""CostRecommendations"" (
                ""Id"" UUID PRIMARY KEY,
                ""ResourceId"" TEXT NOT NULL,
                ""ResourceName"" TEXT NOT NULL,
                ""Provider"" TEXT NOT NULL,
                ""RecommendationType"" TEXT NOT NULL,
                ""Description"" TEXT NOT NULL,
                ""PotentialSavings"" DECIMAL NOT NULL,
                ""Currency"" TEXT NOT NULL,
                ""Status"" TEXT NOT NULL,
                ""DiscoveredAt"" TIMESTAMPTZ NOT NULL
            );
            CREATE TABLE IF NOT EXISTS ""ObservabilityMetrics"" (
                ""Id"" UUID PRIMARY KEY,
                ""ResourceId"" TEXT NOT NULL,
                ""MetricName"" TEXT NOT NULL,
                ""Value"" DOUBLE PRECISION NOT NULL,
                ""Unit"" TEXT NOT NULL,
                ""Timestamp"" TIMESTAMPTZ NOT NULL,
                ""Dimensions"" TEXT -- Map to JSON
            );
            CREATE TABLE IF NOT EXISTS ""ObservabilityLogs"" (
                ""Id"" UUID PRIMARY KEY,
                ""ResourceId"" TEXT NOT NULL,
                ""Level"" TEXT NOT NULL,
                ""Message"" TEXT NOT NULL,
                ""Source"" TEXT NOT NULL,
                ""Timestamp"" TIMESTAMPTZ NOT NULL,
                ""RawData"" TEXT
            );
        ");
        logger.LogInformation("Database schema verified.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Potential database schema mismatch or connection error.");
    }
}

// Endpoints

// Ingest Data from CLI (Thin Client)
app.MapPost("/api/ingest", async ([FromBody] object cloudData, ILogger<Program> logger) =>
{
    logger.LogInformation("Received cloud resource data from CLI.");
    return Results.Ok(new { message = "Data ingested successfully." });
})
.WithName("IngestCloudData");

// --- START NEW AUTH PIPELINE ---
app.MapPost("/api/auth/local/register", async ([FromBody] RegisterRequest request, DracoDbContext dbContext, IConfiguration config) =>
{
    if (await dbContext.UserAccounts.AnyAsync(u => u.Email == request.Email || u.Phone == request.Phone))
        return Results.BadRequest(new { message = "Email or Phone already registered." });

    var user = new UserAccount
    {
        Email = request.Email,
        Phone = request.Phone,
        Name = request.Name,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
        CreatedAt = DateTimeOffset.UtcNow
    };

    dbContext.UserAccounts.Add(user);
    await dbContext.SaveChangesAsync();
    
    var token = GenerateJwtFromUser(user, config);
    return Results.Ok(new { token, user = new { user.Email, user.Name, user.Phone } });
});

app.MapPost("/api/auth/local/login", async ([FromBody] LoginRequest request, DracoDbContext dbContext, IConfiguration config) =>
{
    var user = await dbContext.UserAccounts.FirstOrDefaultAsync(u => u.Email == request.Email);
    if (user == null || string.IsNullOrEmpty(user.PasswordHash) || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        return Results.Unauthorized();

    var token = GenerateJwtFromUser(user, config);
    return Results.Ok(new { token, user = new { user.Email, user.Name, user.Phone } });
});

app.MapPost("/api/auth/local/magic-link", async ([FromBody] MagicLinkRequest request, DracoDbContext dbContext, IMemoryCache cache, ILogger<Program> logger) =>
{
    var user = await dbContext.UserAccounts.FirstOrDefaultAsync(u => u.Email == request.Email);
    if (user == null) return Results.NotFound(new { message = "Account not found." });

    var code = Guid.NewGuid().ToString("N").Substring(0, 8);
    cache.Set($"magic_{code}", user.Email, TimeSpan.FromMinutes(15));
    
    logger.LogInformation("Magic link generated for {Email}: {Code}", request.Email, code);
    return Results.Ok(new { message = "Magic link generated (Demo Mode)", code });
});

app.MapGet("/api/auth/local/verify", async (string code, DracoDbContext dbContext, IMemoryCache cache, IConfiguration config) =>
{
    if (!cache.TryGetValue($"magic_{code}", out string? email) || email == null)
        return Results.BadRequest(new { message = "Invalid or expired code." });

    var user = await dbContext.UserAccounts.FirstOrDefaultAsync(u => u.Email == email);
    if (user == null) return Results.NotFound();

    cache.Remove($"magic_{code}");
    var token = GenerateJwtFromUser(user, config);
    return Results.Ok(new { token, user = new { user.Email, user.Name, user.Phone } });
});

app.MapGet("/api/auth/local/social", (string provider, IConfiguration config) =>
{
    var neonAuthUrl = config["NEON_AUTH_URL"];
    var callbackUrl = "http://localhost:5020/api/auth/local/social/callback";
    return Results.Redirect($"{neonAuthUrl}/signin/{provider}?callbackURL={Uri.EscapeDataString(callbackUrl)}");
});

app.MapGet("/api/auth/local/social/callback", (ILogger<Program> logger) =>
{
    // Bridge redirect back to frontend
    var frontendSuccessUrl = "http://localhost:4321/profile";
    return Results.Redirect(frontendSuccessUrl);
});

// --- END NEW AUTH PIPELINE ---

app.MapPost("/api/auth/neon/exchange", async (HttpContext context, IConfiguration config, IHttpClientFactory httpClientFactory, ILogger<Program> logger) =>
{
    var neonAuthUrl = config["NEON_AUTH_URL"] ?? "https://placeholder-neonauth.aws.neon.tech/neondb/auth";
    var sessionUrl = $"{neonAuthUrl.TrimEnd('/')}/get-session";
    var client = httpClientFactory.CreateClient();

    var cookieHeader = context.Request.Headers["Cookie"].ToString();
    Console.WriteLine($"[DEBUG] Incoming Cookies: {cookieHeader}");

    string? sessionIdFromBody = null;
    string? jwtTokenFromBody = null;
    string? bodyText = null;
    try 
    {
        context.Request.EnableBuffering();
        using var reader = new StreamReader(context.Request.Body, System.Text.Encoding.UTF8, leaveOpen: true);
        bodyText = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0;

        if (!string.IsNullOrWhiteSpace(bodyText))
        {
            using var bodyJson = JsonDocument.Parse(bodyText);
            var root = bodyJson.RootElement;
            
            sessionIdFromBody = TryGetJsonString(root, "sessionId");
            jwtTokenFromBody = TryGetJsonString(root, "sessionToken") ?? TryGetJsonString(root, "token");
            
            if (root.TryGetProperty("session", out var sessionElem))
            {
                sessionIdFromBody ??= TryGetJsonString(sessionElem, "id");
                jwtTokenFromBody ??= TryGetJsonString(sessionElem, "token") ?? TryGetJsonString(sessionElem, "sessionToken");
            }
        }
    }
    catch (Exception ex) 
    { 
        Console.WriteLine($"[DEBUG] Error reading body: {ex.Message}");
    }

    if (string.IsNullOrEmpty(cookieHeader) && string.IsNullOrEmpty(sessionIdFromBody) && string.IsNullOrEmpty(jwtTokenFromBody))
    {
        Console.WriteLine("[DEBUG] No cookies or session data found in request.");
        return Results.Unauthorized();
    }

    using var request = new HttpRequestMessage(HttpMethod.Get, sessionUrl);
    if (!string.IsNullOrEmpty(cookieHeader))
    {
        request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
    }
    
    // Construct session cookie using the ID
    var sessionValue = sessionIdFromBody ?? jwtTokenFromBody; // Fallback to JWT if ID is missing
    if (!string.IsNullOrEmpty(sessionValue) && !cookieHeader.Contains("session_token"))
    {
        var sessionCookie = $"__Secure-neonauth.session_token={sessionValue}; better-auth.session-token={sessionValue}; __neon_auth_session={sessionValue}";
        Console.WriteLine($"[DEBUG] Reconstructing session cookies using: {sessionValue.Substring(0, Math.Min(10, sessionValue.Length))}...");
        
        if (request.Headers.Contains("Cookie"))
            request.Headers.Remove("Cookie");
        
        var combinedCookie = string.IsNullOrEmpty(cookieHeader) ? sessionCookie : $"{cookieHeader}; {sessionCookie}";
        request.Headers.TryAddWithoutValidation("Cookie", combinedCookie);
    }
    
    // Use the JWT for Bearer header
    var bearerValue = jwtTokenFromBody ?? sessionIdFromBody;
    if (!string.IsNullOrEmpty(bearerValue))
    {
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerValue);
    }

    HttpResponseMessage neonResponse;
    try
    {
        Console.WriteLine($"[DEBUG] Calling Neon Auth: {sessionUrl}");
        neonResponse = await client.SendAsync(request);
        
        Console.WriteLine($"[DEBUG] Neon Auth Response Status: {neonResponse.StatusCode}");
        foreach (var header in neonResponse.Headers)
        {
            Console.WriteLine($"[DEBUG] Neon Response Header: {header.Key} = {string.Join(", ", header.Value)}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] Neon Auth Network Error: {ex.Message}");
        logger.LogError(ex, "Failed to call Neon auth get-session endpoint.");
        return Results.Problem($"Failed to validate Neon session (Network/SSL): {ex.Message}", statusCode: StatusCodes.Status502BadGateway);
    }

    if (!neonResponse.IsSuccessStatusCode)
    {
        var errBody = await neonResponse.Content.ReadAsStringAsync();
        Console.WriteLine($"[DEBUG] Neon Auth Failed: {neonResponse.StatusCode} - {errBody}");
        logger.LogWarning("Neon Auth session validation failed with status: {StatusCode}", neonResponse.StatusCode);
        return Results.Unauthorized();
    }

    var responseBody = await neonResponse.Content.ReadAsStringAsync();
    Console.WriteLine($"[DEBUG] Neon Auth Response Body: {responseBody}");

    string? subject = null;
    string? email = null;
    string? name = null;
    string? imageUrl = null;

    if (!string.IsNullOrWhiteSpace(bodyText))
    {
        try 
        {
            using var bodyJson = JsonDocument.Parse(bodyText);
            var root = bodyJson.RootElement;
            
            if (root.TryGetProperty("user", out var userElem))
            {
                email = TryGetJsonString(userElem, "email");
                name = TryGetJsonString(userElem, "name");
                imageUrl = TryGetJsonString(userElem, "image") ?? TryGetJsonString(userElem, "picture");
            }
        }
        catch { }
    }

    if (!string.IsNullOrWhiteSpace(responseBody) && responseBody != "null")
    {
        try 
        {
            using var json = JsonDocument.Parse(responseBody);
            var root = json.RootElement;
            JsonElement userElement;

            if (root.TryGetProperty("data", out var dataElement) && dataElement.TryGetProperty("user", out var user))
            {
                userElement = user;
            }
            else if (root.TryGetProperty("user", out var userDirect))
            {
                userElement = userDirect;
            }
            else
            {
                userElement = default;
            }

            if (userElement.ValueKind != JsonValueKind.Null && userElement.ValueKind != JsonValueKind.Undefined)
            {
                subject ??= TryGetJsonString(userElement, "id");
                email ??= TryGetJsonString(userElement, "email");
                name ??= TryGetJsonString(userElement, "name");
                imageUrl ??= TryGetJsonString(userElement, "image") ?? TryGetJsonString(userElement, "picture");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Error parsing Neon JSON: {ex.Message}");
        }
    }

    // JWT Fallback if Neon API didn't give us the user info
    if (string.IsNullOrEmpty(email) && !string.IsNullOrEmpty(jwtTokenFromBody))
    {
        Console.WriteLine("[DEBUG] Attempting JWT Fallback...");
        try 
        {
            var handler = new JwtSecurityTokenHandler();
            if (handler.CanReadToken(jwtTokenFromBody))
            {
                var jwt = handler.ReadJwtToken(jwtTokenFromBody);
                Console.WriteLine("[DEBUG] JWT Claims:");
                foreach (var claim in jwt.Claims)
                {
                    Console.WriteLine($"  {claim.Type}: {claim.Value}");
                }

                email = jwt.Claims.FirstOrDefault(c => c.Type == "email")?.Value;
                name = jwt.Claims.FirstOrDefault(c => c.Type == "name")?.Value;
                imageUrl = jwt.Claims.FirstOrDefault(c => c.Type == "picture")?.Value 
                          ?? jwt.Claims.FirstOrDefault(c => c.Type == "image")?.Value
                          ?? jwt.Claims.FirstOrDefault(c => c.Type == "avatar_url")?.Value;
                subject = jwt.Claims.FirstOrDefault(c => c.Type == "sub")?.Value 
                          ?? jwt.Claims.FirstOrDefault(c => c.Type == "id")?.Value;
                
                if (!string.IsNullOrEmpty(email))
                {
                    Console.WriteLine($"[DEBUG] JWT Fallback Success for: {email}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] JWT Fallback Failed: {ex.Message}");
        }
    }

    if (string.IsNullOrEmpty(email))
    {
        Console.WriteLine("[DEBUG] No user info found via API or JWT.");
        return Results.Unauthorized();
    }

    // Synchronize to local database
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<DracoDbContext>();

    var userAccount = await db.UserAccounts.FirstOrDefaultAsync(u => u.Email == email);
    if (userAccount == null)
    {
        userAccount = new UserAccount
        {
            Email = email,
            Name = name ?? email.Split('@')[0],
            ImageUrl = imageUrl,
            AuthId = subject,
            Phone = "auth_" + Guid.NewGuid().ToString("N").Substring(0, 8),
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        };
        db.UserAccounts.Add(userAccount);
    }
    else
    {
        userAccount.LastSeenAt = DateTime.UtcNow;
        if (!string.IsNullOrEmpty(name)) userAccount.Name = name;
        if (!string.IsNullOrEmpty(subject)) userAccount.AuthId = subject;
        if (!string.IsNullOrEmpty(imageUrl)) userAccount.ImageUrl = imageUrl;
    }

    await db.SaveChangesAsync();

    Console.WriteLine($"[DEBUG] Generating Draco JWT for: {email}");
    var token = GenerateJwtFromUser(userAccount, config);
    return Results.Ok(new { token });
});

// 4. Cloud OAuth Connectors
app.MapGet("/api/auth/azure", [Authorize] (ClaimsPrincipal user) =>
{
    var authId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
    if (string.IsNullOrEmpty(authId)) return Results.Unauthorized();

    var tenant = "common"; 
    var clientId = builder.Configuration["AZURE_CLIENT_ID"] ?? "placeholder-client-id";
    var redirectUri = "http://localhost:5020/api/auth/callback/azure";
    var scope = "https://management.azure.com/user_impersonation";
    
    var url = $"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/authorize?" +
              $"client_id={clientId}&response_type=code&redirect_uri={Uri.EscapeDataString(redirectUri)}&" +
              $"response_mode=query&scope={Uri.EscapeDataString(scope)}&state={authId}";
              
    return Results.Redirect(url);
})
.WithName("ConnectAzure");

app.MapGet("/api/auth/callback/azure", async (string code, string state, DracoDbContext dbContext, ILogger<Program> logger) =>
{
    var authId = state;
    logger.LogInformation("Received Azure OAuth callback for AuthId {AuthId}", authId);
    
    var account = await dbContext.UserAccounts.FirstOrDefaultAsync(u => u.AuthId == authId);
    if (account != null)
    {
        // For simulation, we add the connection directly if it doesn't exist
        var existing = await dbContext.CloudConnections
            .FirstOrDefaultAsync(c => c.UserPhone == account.Phone && c.Provider == "Azure");
        
        if (existing == null)
        {
            dbContext.CloudConnections.Add(new CloudConnection
            {
                UserPhone = account.Phone,
                Provider = "Azure",
                SubscriptionId = "OAuth-Managed-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                AccessToken = "simulated-token-" + code,
                IsActive = true
            });
            await dbContext.SaveChangesAsync();
        }
    }

    return Results.Content("<html><script>window.close();</script>Azure Connected! You can close this tab.</html>", "text/html");
});

app.MapGet("/api/auth/aws", (string phone) =>
{
    // AWS doesn't have a single 'global' OAuth login for infrastructure in the same way,
    // but we can use IAM Identity Center (OIDC) if configured.
    // For this demo, we'll simulate the redirect to an AWS login portal.
    return Results.Redirect("https://aws.amazon.com/console/");
})
.WithName("ConnectAWS");

app.MapGet("/api/auth/me", [Authorize] async (ClaimsPrincipal user, DracoDbContext dbContext) =>
{
    var authId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
    if (string.IsNullOrEmpty(authId)) return Results.Unauthorized();

    var account = await dbContext.UserAccounts
        .Include(u => u.Connections)
        .Include(u => u.ReportSchedules)
        .FirstOrDefaultAsync(u => u.AuthId == authId);

    if (account == null) 
    {
        // Check if there is an account with the same email from social login
        var email = user.FindFirst(ClaimTypes.Email)?.Value;
        if (!string.IsNullOrEmpty(email))
        {
            account = await dbContext.UserAccounts
                .Include(u => u.Connections)
                .Include(u => u.ReportSchedules)
                .FirstOrDefaultAsync(u => u.Email == email);
            
            if (account != null)
            {
                // Link the AuthId to the existing record
                account.AuthId = authId;
                await dbContext.SaveChangesAsync();
            }
        }
    }

    if (account == null) return Results.NotFound(new { message = "Setup required", authId = authId });

    return Results.Ok(new {
        name = account.Name,
        phone = account.Phone,
        email = account.Email,
        imageUrl = account.ImageUrl,
        preferredChannel = account.PreferredChannel,
        authId = account.AuthId,
        connections = account.Connections.Select(c => new { c.Provider, c.SubscriptionId, c.IsActive }),
        schedule = account.ReportSchedules.FirstOrDefault(s => s.IsActive)
    });
})
.WithName("GetCurrentUser");

app.MapPost("/api/auth/verify-phone", [Authorize] async ([FromBody] VerificationRequest request, IMessagingService messagingService, IMemoryCache cache, ILogger<Program> logger) =>
{
    var code = new Random().Next(100000, 999999).ToString();
    cache.Set($"otp_{request.Phone}", code, TimeSpan.FromMinutes(10));

    var message = $"Your Draco verification code is: {code}. It expires in 10 minutes. 🐉";
    
    try 
    {
        if (request.Channel.Equals("WhatsApp", StringComparison.OrdinalIgnoreCase))
            await messagingService.SendWhatsAppMessageAsync(request.Phone, message);
        else
            await messagingService.SendMessageAsync(request.Phone, message);
            
        logger.LogInformation("Verification code {Code} sent to {Phone} via {Channel}", code, request.Phone, request.Channel);
        return Results.Ok(new { message = "Verification code sent." });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to send verification code to {Phone}", request.Phone);
        return Results.Problem("Failed to send verification code. Please check the phone number format.");
    }
});

app.MapPost("/api/auth/confirm-phone", [Authorize] async ([FromBody] ConfirmPhoneRequest request, ClaimsPrincipal user, DracoDbContext dbContext, IMemoryCache cache, ILogger<Program> logger) =>
{
    var authId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
    if (string.IsNullOrEmpty(authId)) return Results.Unauthorized();

    if (!cache.TryGetValue($"otp_{request.NewPhone}", out string? savedCode) || savedCode != request.Code)
    {
        return Results.BadRequest(new { message = "Invalid or expired verification code." });
    }

    var account = await dbContext.UserAccounts.FirstOrDefaultAsync(u => u.AuthId == authId);
    if (account == null) return Results.NotFound(new { message = "Account not found." });

    var oldPhone = account.Phone;
    var newPhone = request.NewPhone;

    try 
    {
        if (oldPhone != newPhone)
        {
            // Check if new phone is already taken
            if (await dbContext.UserAccounts.AnyAsync(u => u.Phone == newPhone))
                return Results.BadRequest(new { message = "This phone number is already associated with another account." });

            // Update PK and FKs using raw SQL to handle PK mutation
            await dbContext.Database.ExecuteSqlRawAsync(
                "UPDATE \"UserAccounts\" SET \"Phone\" = {0}, \"PreferredChannel\" = {1} WHERE \"Phone\" = {2}",
                newPhone, request.PreferredChannel, oldPhone);
                
            await dbContext.Database.ExecuteSqlRawAsync(
                "UPDATE \"CloudConnections\" SET \"UserPhone\" = {0} WHERE \"UserPhone\" = {1}",
                newPhone, oldPhone);
                
            await dbContext.Database.ExecuteSqlRawAsync(
                "UPDATE \"PulseReportSchedules\" SET \"UserPhone\" = {0} WHERE \"UserPhone\" = {1}",
                newPhone, oldPhone);
        }
        else
        {
            account.PreferredChannel = request.PreferredChannel;
            await dbContext.SaveChangesAsync();
        }

        cache.Remove($"otp_{newPhone}");
        return Results.Ok(new { message = "Profile updated successfully.", phone = newPhone, preferredChannel = request.PreferredChannel });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to update phone number from {Old} to {New}", oldPhone, newPhone);
        return Results.Problem("An error occurred while updating your profile.");
    }
});

app.MapGet("/api/auth/check-user", async (string? phone, string? email, string? authId, DracoDbContext dbContext) =>
{
    var exists = false;
    if (!string.IsNullOrEmpty(authId)) exists = await dbContext.UserAccounts.AnyAsync(a => a.AuthId == authId);
    else if (!string.IsNullOrEmpty(email)) exists = await dbContext.UserAccounts.AnyAsync(a => a.Email == email);
    else if (!string.IsNullOrEmpty(phone)) exists = await dbContext.UserAccounts.AnyAsync(a => a.Phone == phone);
    
    return Results.Ok(new { exists });
})
.WithName("CheckUser");

app.MapPost("/api/auth/setup-complete", [Authorize] async ([FromBody] SetupCompleteRequest request, ClaimsPrincipal user, DracoDbContext dbContext, ILogger<Program> logger) =>
{
    var authId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
    
    logger.LogInformation("Completing setup for {Email} (AuthId: {AuthId})", request.Email, authId);

    var account = await dbContext.UserAccounts.FirstOrDefaultAsync(u => u.AuthId == authId);
    if (account == null)
    {
        account = await dbContext.UserAccounts.FindAsync(request.Phone);
    }

    if (account == null)
    {
        account = new UserAccount 
        { 
            Phone = request.Phone, 
            Name = request.Name,
            PreferredChannel = request.PreferredChannel ?? "SMS",
            AuthId = authId
        };
        dbContext.UserAccounts.Add(account);
    }
    else 
    {
        account.Name = request.Name;
        account.Phone = request.Phone;
        account.AuthId = authId;
        account.PreferredChannel = request.PreferredChannel ?? account.PreferredChannel;
        account.LastSeenAt = DateTimeOffset.UtcNow;
    }

    foreach (var conn in request.Connections)
    {
        var existingConn = await dbContext.CloudConnections
            .FirstOrDefaultAsync(c => c.UserPhone == request.Phone && c.Provider == conn.Provider);
            
        if (existingConn == null)
        {
            dbContext.CloudConnections.Add(new CloudConnection
            {
                UserPhone = request.Phone,
                Provider = conn.Provider,
                SubscriptionId = conn.SubscriptionId,
                AccessToken = conn.AccessToken,
                IsActive = true
            });
        }
        else
        {
            existingConn.SubscriptionId = conn.SubscriptionId;
            existingConn.AccessToken = conn.AccessToken;
            existingConn.IsActive = true;
        }
    }

    await dbContext.SaveChangesAsync();
    return Results.Ok(new { message = "Sentinel initialized and persisted." });
})
.WithName("SetupComplete");

// 5. Reporting & Schedules
app.MapGet("/api/reports/schedule", async (string phone, DracoDbContext dbContext) =>
{
    var schedule = await dbContext.PulseReportSchedules
        .AsNoTracking()
        .FirstOrDefaultAsync(s => s.UserPhone == phone && s.IsActive);
        
    return schedule != null ? Results.Ok(schedule) : Results.NotFound();
})
.WithName("GetSchedule");

app.MapPost("/api/reports/schedule", async ([FromBody] UpdateScheduleRequest request, DracoDbContext dbContext) =>
{
    var schedule = await dbContext.PulseReportSchedules
        .FirstOrDefaultAsync(s => s.UserPhone == request.Phone);
        
    if (schedule == null)
    {
        schedule = new PulseReportSchedule { UserPhone = request.Phone };
        dbContext.PulseReportSchedules.Add(schedule);
    }
    
    schedule.Frequency = request.Frequency;
    schedule.IncludeCostAnalysis = request.IncludeCostAnalysis;
    schedule.IncludeSecurityHealth = request.IncludeSecurityHealth;
    schedule.IsActive = request.IsActive;
    
    // Reset next run based on new frequency
    schedule.NextRunAt = schedule.Frequency switch {
        "Daily" => DateTimeOffset.UtcNow.AddDays(1),
        "Weekly" => DateTimeOffset.UtcNow.AddDays(7),
        "Monthly" => DateTimeOffset.UtcNow.AddDays(30),
        _ => schedule.NextRunAt
    };

    await dbContext.SaveChangesAsync();
    return Results.Ok(new { message = "Schedule updated." });
})
.WithName("UpdateSchedule");

// 6. Twilio Webhook for Remediation Approval and AI Chat
app.MapPost("/api/webhook/twilio", async (
    HttpContext context, 
    RemediationService remediationService, 
    IAIService aiService, 
    IMessagingService messagingService,
    DracoDbContext dbContext, 
    ILogger<Program> logger) =>
{
    var form = await context.Request.ReadFormAsync();
    var body = form["Body"].ToString().Trim();
    var fromRaw = form["From"].ToString();
    
    // Twilio form data URL-decodes the '+' into a space if we aren't careful.
    var from = fromRaw.Replace(" ", "+");

    logger.LogInformation($"Received message from {from}: {body}");

    // Handle Approval
    if (body.Equals("Yes", StringComparison.OrdinalIgnoreCase) || body.Equals("Apply", StringComparison.OrdinalIgnoreCase))
    {
        logger.LogInformation("Approval received! Remediating...");
        await remediationService.RemediateAsync("Action requested via chat", from, "Draco-Governance");
        var approvalMsg = "Remediation started. I will notify you once complete.";
        
        if (from.StartsWith("whatsapp:"))
            await messagingService.SendWhatsAppMessageAsync(from, approvalMsg);
        else
            await messagingService.SendMessageAsync(from, approvalMsg);

        return Results.Ok();
    }

    // Handle Draco Help Command
    if (body.Equals("Draco help", StringComparison.OrdinalIgnoreCase) || body.Equals("help", StringComparison.OrdinalIgnoreCase))
    {
        var helpMsg = "Here are some things you can ask me:\n" +
                      "• 'What are my current resources?'\n" +
                      "• 'Am I over-provisioned anywhere?'\n" +
                      "• 'How much am I spending?'\n" +
                      "• 'Remediate my last warning'\n\n" +
                      "Just chat with me naturally! 🐉";
        if (from.StartsWith("whatsapp:")) await messagingService.SendWhatsAppMessageAsync(from, helpMsg); else await messagingService.SendMessageAsync(from, helpMsg);
        return Results.Ok();
    }

    // Handle Easter Eggs
    if (body.Equals("Ryuga", StringComparison.OrdinalIgnoreCase))
    {
        var quotes = new[]
        {
            "In the end you're nothing but a caged bird. Thinking you've become stronger from the pet food you were given and believing you can defeat the Dragon Emperor. It's just hilarious.",
            "You say you want to borrow my strength?! Give me a call when you have something to offer me in return.",
            "I will not loose. Even if my opponent is a god, I will defeat anyone who stands in my way. My name is Ryuga, and I reign over the world as the Strongest, The Dragon Emperor, Do you hear me?",
            "Be careful what you poke with a stick, it just might bite you!",
            "Nonsense! This so called \"Hades\" you saw was nothing compared to what I have gone to letting into the world! A complete utter darkness.",
            "The more you resist, the more it will control you. You cannot fight it trust me. You must become one with it understand?"
        };
        var randomQuote = quotes[new Random().Next(quotes.Length)];
        if (from.StartsWith("whatsapp:")) await messagingService.SendWhatsAppMessageAsync(from, randomQuote); else await messagingService.SendMessageAsync(from, randomQuote);
        return Results.Ok();
    }

    if (body.Equals("Dragon Emperor Soaring Bite Strike", StringComparison.OrdinalIgnoreCase) || 
        body.Equals("Dragon Emperor Soaring Destruction", StringComparison.OrdinalIgnoreCase))
    {
        var msg = "1. Gingka Hagane, Yu Tendo, Ryo Hagane, Dark Nebula, Hikaru Hasama, Kyoya Tategami";
        if (from.StartsWith("whatsapp:")) await messagingService.SendWhatsAppMessageAsync(from, msg); else await messagingService.SendMessageAsync(from, msg);
        return Results.Ok();
    }

    if (body.Equals("Dragon Emperor Supreme Flight", StringComparison.OrdinalIgnoreCase))
    {
        var msg = "2. Jack, Dr. Ziggurat, Julian, Hades. Inc\n3. Tsubasa Otori, King, Kenta Yumiya, Gingka Hagane, Chris, Kyoya Tategami, Yuki Mizusawa";
        if (from.StartsWith("whatsapp:")) await messagingService.SendWhatsAppMessageAsync(from, msg); else await messagingService.SendMessageAsync(from, msg);
        return Results.Ok();
    }

    if (body.Equals("Dragon Emperor Life Destructor", StringComparison.OrdinalIgnoreCase))
    {
        var msg = "3. Tsubasa Otori, King, Kenta Yumiya, Gingka Hagane, Chris, Kyoya Tategami, Yuki Mizusawa";
        if (from.StartsWith("whatsapp:")) await messagingService.SendWhatsAppMessageAsync(from, msg); else await messagingService.SendMessageAsync(from, msg);
        return Results.Ok();
    }

    if (body.Equals("nemesis", StringComparison.OrdinalIgnoreCase))
    {
        var msg = "I will not loose. Even if my opponent is a god, I will defeat anyone who stands in my way. My name is Ryuga, and I reign over the world as the Strongest, The Dragon Emperor, Do you hear me? ULTIMATE MOVE: DRAGON EMPEROR LIFE DESTRUCTOR.";
        if (from.StartsWith("whatsapp:")) await messagingService.SendWhatsAppMessageAsync(from, msg); else await messagingService.SendMessageAsync(from, msg);
        return Results.Ok();
    }

    // Handle General Query with AI
    try 
    {
        // 1. Send immediate "typing" acknowledgment
        var ackMsg = "Got it! Draco is looking into your cloud resources now... 🐉🔍";
        if (from.StartsWith("whatsapp:"))
            await messagingService.SendWhatsAppMessageAsync(from, ackMsg);
        else
            await messagingService.SendMessageAsync(from, ackMsg);

        // Fetch resources scoped to the user (via their phone/connections)
        var userPhone = from.Replace("whatsapp:", "");
        var userConnections = await dbContext.CloudConnections
            .AsNoTracking()
            .Where(c => c.UserPhone == userPhone && c.IsActive)
            .Select(c => c.SubscriptionId)
            .ToListAsync();
        
        var resources = userConnections.Any()
            ? await dbContext.CloudResources
                .AsNoTracking()
                .Where(r => userConnections.Contains(r.SubscriptionId))
                .Take(20)
                .ToListAsync()
            : await dbContext.CloudResources.AsNoTracking().Take(10).ToListAsync();

        var contextStr = string.Join("; ", resources.Select(r => $"{r.Name} ({r.Type}, {r.Provider})"));
        
        var aiResponse = await aiService.ProcessQueryAsync(body, contextStr);
        logger.LogInformation("AI Response generated: {Response}", aiResponse);

        // Explicitly send the message back via Twilio API (more reliable for WhatsApp than TwiML)
        if (from.StartsWith("whatsapp:"))
        {
            await messagingService.SendWhatsAppMessageAsync(from, aiResponse);
        }
        else 
        {
            await messagingService.SendMessageAsync(from, aiResponse);
        }

        return Results.Ok();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error processing AI query via webhook.");
        var errorMsg = "Sorry, I encountered an error processing your request.";
        
        if (from.StartsWith("whatsapp:"))
            await messagingService.SendWhatsAppMessageAsync(from, errorMsg);
        else
            await messagingService.SendMessageAsync(from, errorMsg);

        return Results.Ok();
    }
})
.WithName("TwilioWebhook");

// 3. Health Check for Tunnel/Status
app.MapGet("/health", async (DracoDbContext dbContext, IMemoryCache cache) =>
{
    var count = await cache.GetOrCreateAsync("health_resource_count", async entry =>
    {
        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
        return await dbContext.CloudResources.CountAsync();
    });
    return Results.Ok(new
    {
        status = "Healthy",
        timestamp = DateTime.UtcNow,
        resourceCount = count,
        aiModel = "Gemini 3 Flash Preview",
        tunnelActive = true
    });
})
.WithName("HealthCheck");

app.Run();

// --- HELPERS & TYPES (Must be at the bottom) ---

string GenerateJwt(string subject, string? email, string? name, IConfiguration config, string? phone = null, string? imageUrl = null)
{
    var secret = config["JWT_SECRET"] ?? "super-secret-dragon-key-2026-draco-sentinel";
    var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.ASCII.GetBytes(secret));
    var creds = new Microsoft.IdentityModel.Tokens.SigningCredentials(key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, subject),
        new("sub", subject)
    };

    if (!string.IsNullOrWhiteSpace(email)) claims.Add(new Claim(ClaimTypes.Email, email));
    if (!string.IsNullOrWhiteSpace(name)) claims.Add(new Claim(ClaimTypes.Name, name));
    if (!string.IsNullOrWhiteSpace(phone)) claims.Add(new Claim(ClaimTypes.MobilePhone, phone));
    if (!string.IsNullOrWhiteSpace(imageUrl)) claims.Add(new Claim("picture", imageUrl));

    var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
        claims: claims,
        expires: DateTime.Now.AddDays(7),
        signingCredentials: creds
    );

    return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
}

string GenerateJwtFromUser(Draco.Domain.Entities.UserAccount user, IConfiguration config)
{
    return GenerateJwt(user.AuthId ?? user.Phone, user.Email, user.Name, config, user.Phone, user.ImageUrl);
}

static string? TryGetJsonString(JsonElement element, string propertyName)
{
    if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
    {
        return null;
    }

    return property.GetString();
}

public record RegisterRequest(string Email, string Password, string Name, string Phone);
public record LoginRequest(string Email, string Password);
public record MagicLinkRequest(string Email);
public record VerificationRequest(string Phone, string Channel);
public record ConfirmCodeRequest(string Phone, string Code);
public record ConfirmPhoneRequest(string NewPhone, string Code, string PreferredChannel);
public record SetupCompleteRequest(string Phone, string Name, string Email, string? PreferredChannel, List<ConnectionInfo> Connections);
public record UpdateScheduleRequest(string Phone, string Frequency, bool IncludeCostAnalysis, bool IncludeSecurityHealth, bool IsActive);
public record ConnectionInfo(string Provider, string SubscriptionId, string? AccessToken);
