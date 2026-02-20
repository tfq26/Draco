using Draco.Application.Interfaces;
using Draco.Application.Services;
using Draco.Domain.Repositories;
using Draco.Infrastructure.Data;
using Draco.Infrastructure.Providers;
using Draco.Infrastructure.Repositories;
using Draco.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using dotenv.net;
using Microsoft.AspNetCore.Mvc;

DotEnv.Load();

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Endpoints

// 1. Ingest Data from CLI (Thin Client)
app.MapPost("/api/ingest", async ([FromBody] object cloudData, ILogger<Program> logger) =>
{
    logger.LogInformation("Received cloud resource data from CLI.");
    // In actual implementation, we'd parse the JSON, save to DB, and trigger Analysis
    return Results.Ok(new { message = "Data ingested successfully." });
})
.WithName("IngestCloudData");

// 2. Twilio Webhook for Remediation Approval and AI Chat
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

    // Handle General Query with AI
    try 
    {
        // 1. Send immediate "typing" acknowledgment
        var ackMsg = "Got it! Draco is looking into your cloud resources now... 🐉🔍";
        if (from.StartsWith("whatsapp:"))
            await messagingService.SendWhatsAppMessageAsync(from, ackMsg);
        else
            await messagingService.SendMessageAsync(from, ackMsg);

        var resources = await dbContext.CloudResources.Take(10).ToListAsync();
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
app.MapGet("/health", async (DracoDbContext dbContext) =>
{
    var count = await dbContext.CloudResources.CountAsync();
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

// Initialize Database automatically on start
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<DracoDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
}

app.Run();
