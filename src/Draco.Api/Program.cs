using Draco.Api.Endpoints;
using Draco.Application.Interfaces;
using Draco.Application.Services;
using Draco.Domain.Repositories;
using Draco.Infrastructure.Data;
using Draco.Infrastructure.Providers;
using Draco.Infrastructure.Repositories;
using Draco.Infrastructure.Services;
using dotenv.net;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

DotEnv.Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();

builder.Services.AddCors(options =>
{
    options.AddPolicy("DracoClient", policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var connectionString = ConnectionStringNormalizer.Normalize(
    builder.Configuration["DRACO_DB_MAIN_CONNECTION"]
    ?? builder.Configuration.GetConnectionString("DracoDbContext")
    ?? builder.Configuration["DRACO_DB_CONNECTION"]
    ?? "Host=localhost;Database=draco;Username=postgres;Password=postgres");

builder.Services.AddDbContext<DracoDbContext>(options =>
    options.UseNpgsql(connectionString, npgsqlOptions => npgsqlOptions.UseVector()));

builder.Services.AddHttpClient<IAIService, GeminiAIService>();
builder.Services.AddScoped<IResourceRepository, ResourceRepository>();
builder.Services.AddScoped<ICloudProvider, AzureProvider>();
builder.Services.AddScoped<ICloudProvider, AWSProvider>();
builder.Services.AddScoped<IMessagingService, AzureMessagingService>();
builder.Services.AddScoped<IEmailService, SendGridService>();
builder.Services.AddScoped<IGitProvider, GitHubProvider>();
builder.Services.AddScoped<AlertOrchestrator>();
builder.Services.AddScoped<ResourceDiscoveryService>();
builder.Services.AddScoped<RemediationService>();
builder.Services.AddScoped<PulseReportService>();
builder.Services.AddScoped<ICostGovernanceService, CostGovernanceService>();
builder.Services.AddScoped<ITelemetryService, TelemetryService>();
builder.Services.AddScoped<IInsightContextService, InsightContextService>();
builder.Services.AddScoped<IResourceActionService, TerraformResourceActionService>();
builder.Services.AddScoped<INotificationEvaluationService, NotificationEvaluationService>();
builder.Services.AddScoped<INotificationRule, BudgetThresholdNotificationRule>();
builder.Services.AddScoped<INotificationRule, ComputeResourceNotificationRule>();
builder.Services.AddScoped<INotificationRule, StorageResourceNotificationRule>();
builder.Services.AddScoped<INotificationRule, FunctionResourceNotificationRule>();
builder.Services.AddScoped<WorkflowEventService>();
builder.Services.AddHostedService<PulseBackgroundScheduler>();
builder.Services.AddHostedService<WorkflowEventBackgroundService>();

var jwtSecret = builder.Configuration["JWT_SECRET"] ?? "super-secret-dragon-key-2026-draco-sentinel";
var signingKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwtSecret));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseExceptionHandler();
}

app.UseCors("DracoClient");
app.UseAuthentication();
app.UseAuthorization();

await EnsureDatabaseAsync(app.Services);

app.MapHealthEndpoints();
app.MapAuthEndpoints();
app.MapCloudConnectionEndpoints();
app.MapResourceEndpoints();
app.MapTelemetryEndpoints();
app.MapDashboardEndpoints();
app.MapEventWorkflowEndpoints();
app.MapNotificationEndpoints();

app.Run();

static async Task EnsureDatabaseAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<DracoDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInitialization");

    try
    {
        logger.LogInformation("Starting database initialization.");
        dbContext.Database.SetCommandTimeout(TimeSpan.FromSeconds(30));
        await dbContext.Database.MigrateAsync();
        logger.LogInformation("Database migrations applied.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to initialize database schema.");
        throw;
    }
}
