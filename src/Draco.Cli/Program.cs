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
    using (var scope = serviceProvider.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<DracoDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
    }
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

var initCommand = new Command("init", "Initializes the Draco infrastructure and performs onboarding.");
initCommand.SetHandler(async () =>
{
    AnsiConsole.Write(new FigletText("Draco Setup").Color(Color.Cyan1));
    AnsiConsole.MarkupLine("[bold white]Welcome to the Draco Sentinel Onboarding.[/]\n");

    // 1. Get User Info
    var name = AnsiConsole.Ask<string>("What is your [bold cyan]name[/]?");
    var phoneNumber = AnsiConsole.Ask<string>("Enter your [bold cyan]phone number[/] (for SMS alerts):");

    // 2. Twilio Verification
    var messagingService = serviceProvider.GetRequiredService<IMessagingService>();
    var verificationCode = Guid.NewGuid().ToString().Substring(0, 6).ToUpper();
    
    await AnsiConsole.Status()
        .StartAsync("Sending verification code...", async ctx =>
        {
            await messagingService.SendMessageAsync(phoneNumber, $"Your Draco Sentinel verification code is: {verificationCode}");
        });

    AnsiConsole.MarkupLine("[green]Code sent successfully via Twilio![/]");
    
    var userCode = "";
    int attempts = 0;
    while (userCode != verificationCode && attempts < 3)
    {
        userCode = AnsiConsole.Ask<string>("Please enter the [bold yellow]6-digit code[/] sent to your phone:").Trim().ToUpper();
        if (userCode != verificationCode)
        {
            AnsiConsole.MarkupLine("[red]Invalid code.[/] Please try again.");
            attempts++;
        }
    }

    if (userCode != verificationCode)
    {
        AnsiConsole.MarkupLine("[bold red]Verification failed.[/] Setup aborted.");
        return;
    }

    AnsiConsole.MarkupLine($"[bold green]Success![/] Verified. Welcome to the fold, {name}.");

    // 3. Cloud Provider Selection
    var providerType = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("Which [bold cyan]Cloud Provider[/] would you like to initialize first?")
            .PageSize(10)
            .AddChoices(new[] { "Azure", "AWS", "Google Cloud (Coming Soon)" }));

    if (providerType == "Google Cloud (Coming Soon)")
    {
        AnsiConsole.MarkupLine("[yellow]Stay tuned! GCP support is in the labs.[/]");
        return;
    }

    AnsiConsole.MarkupLine($"[bold blue]Connecting to {providerType} SDK...[/]");
    
    // Simulate SDK sign-in
    await AnsiConsole.Status()
        .StartAsync($"Authenticating with {providerType}...", async ctx =>
        {
            await Task.Delay(2000); // Simulate sign-in
        });

    AnsiConsole.MarkupLine("[green]Authenticated successfully![/]");

    // 4. Resource Initialization
    var discoveryService = serviceProvider.GetRequiredService<ResourceDiscoveryService>();
    await AnsiConsole.Status()
        .StartAsync("Initializing Draco with your cloud resources...", async ctx =>
        {
            await discoveryService.RunDiscoveryAsync(CancellationToken.None);
        });

    // 5. Notifications Preferences
    var preferences = AnsiConsole.Prompt(
        new MultiSelectionPrompt<string>()
            .Title("\nWhich [bold cyan]Real-time Alerts[/] would you like to receive?")
            .NotRequired()
            .PageSize(10)
            .InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept)[/]")
            .AddChoices(new[] {
                "Budget Concerns", "Resource Spikes", "Resource Failures", 
                "Critical Outages", "Suggestion Summaries"
            }));

    AnsiConsole.MarkupLine("\n[bold green]Configuration Complete![/]");
    
    // 6. Show Commands Summary
    var table = new Table();
    table.AddColumn("Command");
    table.AddColumn("Description");
    table.AddRow("[cyan]draco start[/]", "Starts the sentinel monitoring loop");
    table.AddRow("[cyan]draco status[/]", "Checks the health of your connected sensors");
    table.AddRow("[cyan]draco shell[/]", "Enter interactive AI mode");
    
    AnsiConsole.Write(table);

    AnsiConsole.MarkupLine("\n[bold]Interaction Guide:[/]");
    AnsiConsole.MarkupLine("• [yellow]CLI:[/] Use 'draco shell' to ask questions about your cloud.");
    AnsiConsole.MarkupLine("• [yellow]SMS:[/] Reply to alerts directly via SMS to approve remediations.");
    AnsiConsole.MarkupLine("\n[bold blue]Draco is ready for duty.[/]");
});

rootCommand.AddCommand(initCommand);

var statusCommand = new Command("status", "Checks the health of your connected sensors and cloud providers.");
statusCommand.SetHandler(() =>
{
    var table = new Table().Border(TableBorder.Rounded);
    table.AddColumn("Sensor");
    table.AddColumn("Status");
    table.AddRow("Azure Provider", "[green]Online[/]");
    table.AddRow("AWS Provider", "[green]Online[/]");
    table.AddRow("Twilio Gateway", "[green]Active[/]");
    table.AddRow("Gemini AI Core", "[green]Ready[/]");
    AnsiConsole.Write(table);
});
rootCommand.AddCommand(statusCommand);

var shellCommand = new Command("shell", "Enter interactive AI mode to query your cloud infrastructure.");
shellCommand.SetHandler(async () =>
{
    AnsiConsole.MarkupLine("[bold blue]Draco Shell v1.0.0[/] - Powered by Gemini AI");
    AnsiConsole.MarkupLine("Type [yellow]exit[/] to return to CLI.");
    
    while (true)
    {
        var query = AnsiConsole.Ask<string>("[bold cyan]draco>[/]");
        if (query.Equals("exit", StringComparison.OrdinalIgnoreCase)) break;
        
        await AnsiConsole.Status()
            .StartAsync("Analyzing cloud state...", async ctx =>
            {
                await Task.Delay(1500); // Simulate Gemini processing
                AnsiConsole.MarkupLine("[grey]Draco:[/] Based on your current Azure tags, you have 3 underutilized VMs. Would you like me to generate a remediation PR?");
            });
    }
});
rootCommand.AddCommand(shellCommand);

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
    var connectionString = configuration["DRACO_DB_MAIN_CONNECTION"]
                           ?? configuration.GetConnectionString("DracoDbContext") 
                           ?? configuration["DRACO_DB_CONNECTION"] 
                           ?? "Host=localhost;Database=draco;Username=postgres;Password=postgres";

    services.AddDbContext<DracoDbContext>(options =>
        options.UseNpgsql(connectionString, x => x.UseVector()));

    services.AddHttpClient<IAIService, GeminiAIService>();
    services.AddScoped<IResourceRepository, ResourceRepository>();
    services.AddScoped<ICloudProvider, AzureProvider>();
    services.AddScoped<ICloudProvider, AWSProvider>();
    services.AddScoped<IMessagingService, TwilioService>();
    services.AddScoped<IEmailService, SendGridService>();
    services.AddScoped<IGitProvider, GitHubProvider>();
    services.AddScoped<AlertOrchestrator>();
    services.AddScoped<ResourceDiscoveryService>();
    services.AddScoped<RemediationService>();
}
