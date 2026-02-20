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
