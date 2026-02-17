using System.CommandLine;
using Draco.Application.Interfaces;
using Draco.Application.Services;
using Draco.Domain.Repositories;
using Draco.Infrastructure.Data;
using Draco.Infrastructure.Providers;
using Draco.Infrastructure.Repositories;
using Draco.Infrastructure.Services;
using Draco.Cli;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;

using Microsoft.Extensions.Configuration;
using dotenv.net;

DotEnv.Load();

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .Build();

var serviceCollection = new ServiceCollection();
ConfigureServices(serviceCollection, configuration);
var serviceProvider = serviceCollection.BuildServiceProvider();

var rootCommand = new RootCommand("Draco: Autonomous Cloud Governance Sentinel");

var startCommand = new Command("start", "Starts the Draco sentinel monitoring loop");
startCommand.SetHandler(async () =>
{
    var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
    var discoveryService = serviceProvider.GetRequiredService<ResourceDiscoveryService>();
    var remediationService = serviceProvider.GetRequiredService<RemediationService>();
    var cts = new CancellationTokenSource();

    AnsiConsole.Write(new FigletText("Draco").Color(Color.Blue));
    AnsiConsole.MarkupLine("[bold blue]Sentinel initialized.[/] Starting monitoring loop...");

    // Start Webhook Listener
    var webhookLogger = serviceProvider.GetRequiredService<ILogger<TwilioWebhookListener>>();
    var webhookListener = new TwilioWebhookListener(8080, async message =>
    {
        if (message.Equals("Yes", StringComparison.OrdinalIgnoreCase) || message.Equals("Apply", StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine("[bold green]Approval received via SMS![/] Remediating...");
            // In a real app, we'd pull the context from a state store (Postgres)
            await remediationService.RemediateAsync("Underutilized VM detected in scan", "taufeeqali", "Draco-Governance");
        }
    }, webhookLogger);

    _ = webhookListener.StartAsync(cts.Token);

    // Basic loop for now
    while (!cts.Token.IsCancellationRequested)
    {
        await AnsiConsole.Status()
            .StartAsync("Scanning cloud resources...", async ctx =>
            {
                await discoveryService.RunDiscoveryAsync(cts.Token);
            });

        AnsiConsole.MarkupLine($"[grey]{DateTime.Now:T}[/] [green]Scan complete.[/] Sleeping for 60 seconds...");
        await Task.Delay(TimeSpan.FromSeconds(60), cts.Token);
    }
});

rootCommand.AddCommand(startCommand);

return await rootCommand.InvokeAsync(args);

void ConfigureServices(IServiceCollection services, IConfiguration configuration)
{
    services.AddSingleton(configuration);
    
    services.AddLogging(builder =>
    {
        builder.AddConsole();
        builder.SetMinimumLevel(LogLevel.Information);
    });

    // Configuration - Pull from appsettings.json or environment variables
    var connectionString = configuration.GetConnectionString("DracoDbContext") 
                           ?? configuration["DRACO_DB_CONNECTION"] 
                           ?? "Host=localhost;Database=draco;Username=postgres;Password=postgres";

    services.AddDbContext<DracoDbContext>(options =>
        options.UseNpgsql(connectionString, x => x.UseVector()));

    services.AddHttpClient<IAIService, GeminiAIService>();
    services.AddScoped<IResourceRepository, ResourceRepository>();
    services.AddScoped<ICloudProvider, AzureProvider>();
    services.AddScoped<IMessagingService, TwilioService>();
    services.AddScoped<IEmailService, SendGridService>();
    services.AddScoped<IGitProvider, GitHubProvider>();
    services.AddScoped<AlertOrchestrator>();
    services.AddScoped<ResourceDiscoveryService>();
    services.AddScoped<RemediationService>();
}
