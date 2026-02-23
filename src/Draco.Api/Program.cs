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
builder.Services.AddMemoryCache();

// Configure JWT Authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["NEON_AUTH_URL"]; // Managed Neon Auth / Better Auth URL
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidAudience = builder.Configuration["NEON_AUTH_AUDIENCE"] ?? "draco-web"
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
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
                ""AuthId"" TEXT,
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
        ");
        logger.LogInformation("Database schema verified.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Potential database schema mismatch or connection error.");
    }
}

// Endpoints

// 1. Ingest Data from CLI (Thin Client)
app.MapPost("/api/ingest", async ([FromBody] object cloudData, ILogger<Program> logger) =>
{
    logger.LogInformation("Received cloud resource data from CLI.");
    // In actual implementation, we'd parse the JSON, save to DB, and trigger Analysis
    return Results.Ok(new { message = "Data ingested successfully." });
})
.WithName("IngestCloudData");

// 2. Auth & Verification (LEACY REMOVED - Using Better Auth via Neon)

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
            AuthId = authId
        };
        dbContext.UserAccounts.Add(account);
    }
    else
    {
        account.Name = request.Name;
        account.Phone = request.Phone;
        account.AuthId = authId;
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

public record VerificationRequest(string Phone, string Channel);
public record ConfirmCodeRequest(string Phone, string Code);
public record SetupCompleteRequest(string Phone, string Name, string Email, List<ConnectionInfo> Connections);
public record UpdateScheduleRequest(string Phone, string Frequency, bool IncludeCostAnalysis, bool IncludeSecurityHealth, bool IsActive);
public record ConnectionInfo(string Provider, string SubscriptionId, string? AccessToken);
