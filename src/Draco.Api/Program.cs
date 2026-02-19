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

// 2. Twilio Webhook for Remediation Approval
app.MapPost("/api/webhook/twilio", async (HttpContext context, RemediationService remediationService, ILogger<Program> logger) =>
{
    var form = await context.Request.ReadFormAsync();
    var body = form["Body"].ToString().Trim();
    var from = form["From"].ToString();

    logger.LogInformation($"Received SMS from {from}: {body}");

    if (body.Equals("Yes", StringComparison.OrdinalIgnoreCase) || body.Equals("Apply", StringComparison.OrdinalIgnoreCase))
    {
        logger.LogInformation("Approval received via SMS! Remediating...");
        // In a real app, we'd pull the context from DB
        await remediationService.RemediateAsync("Underutilized VM detected in scan", "taufeeqali", "Draco-Governance");
        return Results.Content("<Response><Message>Remediation started.</Message></Response>", "application/xml");
    }

    return Results.Content("<Response></Response>", "application/xml");
})
.WithName("TwilioWebhook");

// Initialize Database automatically on start
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<DracoDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
}

app.Run();
