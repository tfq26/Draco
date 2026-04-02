using System.Text;
using System.Text.Json;
using Draco.Application.Interfaces;
using Draco.Infrastructure.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Draco.Infrastructure.Services;

public class GeminiAIService : IAIService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GeminiAIService> _logger;
    private readonly string _apiKey;
    private readonly string _systemPrompt;
    private readonly IServiceProvider _serviceProvider;
    private const string ApiUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-pro:generateContent";

    public GeminiAIService(HttpClient httpClient, ILogger<GeminiAIService> logger, IConfiguration configuration, IServiceProvider serviceProvider)
    {
        _httpClient = httpClient;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _apiKey = configuration["Gemini:ApiKey"] ?? configuration["GOOGLE_GEMINI_API_KEY"] ?? "KEY";
        _systemPrompt = LoadSystemPrompt();
    }

    private static string LoadSystemPrompt()
    {
        // Look for the prompt file relative to the application base directory
        var basePath = AppContext.BaseDirectory;
        var promptPaths = new[]
        {
            Path.Combine(basePath, "Prompts", "draco-system-prompt.md"),
            Path.Combine(basePath, "..", "..", "..", "Prompts", "draco-system-prompt.md"),
            Path.Combine(Directory.GetCurrentDirectory(), "Prompts", "draco-system-prompt.md")
        };

        foreach (var path in promptPaths)
        {
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }

        // Fallback if file not found
        return "You are Draco, a helpful cloud governance AI. Be concise, professional, and use emojis naturally.";
    }

    public async Task<string> AnalyzeAnomalyAsync(string rawData, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Analyzing cloud data with Gemini...");
        var prompt = $@"{_systemPrompt}

---
TASK: Analyze the following cloud resource data and identify any cost anomalies or security risks. Redact any PII.

DATA:
{rawData}";
        return await CallGeminiAsync(prompt, cancellationToken);
    }

    public async Task<string> GenerateRemediationHclAsync(string context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating Terraform remediation for context: {Context}", context);
        var prompt = $@"{_systemPrompt}

---
TASK: Generate a valid and safe Terraform HCL snippet to remediate the following cloud anomaly. Output ONLY the HCL code.

ANOMALY:
{context}";
        return await CallGeminiAsync(prompt, cancellationToken);
    }

    public async Task<string> CreateConversationalAlertAsync(string analysis, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating natural language alert for analysis.");
        var prompt = $@"{_systemPrompt}

---
TASK: Translate this technical cloud analysis into a concise, professional SMS alert message.

ANALYSIS:
{analysis}";
        return await CallGeminiAsync(prompt, cancellationToken);
    }

    public async Task<string> ProcessQueryAsync(string query, string context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing user query: {Query}", query);
        var prompt = $@"{_systemPrompt}

---
TASK: Answer the user's question using only the provided infrastructure context.
The context is pre-computed by Draco's backend and should be treated as the source of truth.
Do not invent resources, costs, incidents, remediations, or provider details that are not present.
If the answer is not fully supported by the context, say what is missing and what workflow should run next.

USER QUESTION: {query}

INFRASTRUCTURE CONTEXT:
{context}";
        return await CallGeminiAsync(prompt, cancellationToken);
    }

    public async Task<string> AnalyzeResourcesAsync(IEnumerable<object> resources, string prompt, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Analyzing multiple resources with Gemini...");
        var resourcesJson = JsonSerializer.Serialize(resources);
        var fullPrompt = $@"{_systemPrompt}

---
{prompt}

RAW DATA:
{resourcesJson}";
        return await CallGeminiAsync(fullPrompt, cancellationToken);
    }

    private async Task<string> CallGeminiAsync(string prompt, CancellationToken cancellationToken)
    {
        var requestBody = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } }
            },
            tools = new[] { ToolRegistry.GetToolDefinitions() }
        };

        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"{ApiUrl}?key={_apiKey}", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Gemini API call failed: {Error}", error);
            return "Analysis failed.";
        }

        var result = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(result);
        
        if (doc.RootElement.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
        {
            var contentElement = candidates[0].GetProperty("content");
            if (contentElement.TryGetProperty("parts", out var parts))
            {
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("functionCall", out var functionCall))
                    {
                        var functionName = functionCall.GetProperty("name").GetString();
                        var args = functionCall.GetProperty("args");
                        
                        _logger.LogInformation("Gemini requested tool call: {FunctionName}", functionName);
                        return await HandleToolCallAsync(functionName!, args, cancellationToken);
                    }

                    if (part.TryGetProperty("text", out var textElement))
                    {
                        return textElement.GetString() ?? "No analysis provided.";
                    }
                }
            }
        }

        return "No analysis provided.";
    }

    private async Task<string> HandleToolCallAsync(string name, JsonElement args, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var governanceService = scope.ServiceProvider.GetRequiredService<ICostGovernanceService>();

        try 
        {
            _logger.LogInformation("Executing tool: {Name} with args: {Args}", name, args.GetRawText());
            
            switch (name)
            {
                case "get_current_spend":
                    var providerSpend = args.GetProperty("provider").GetString();
                    var subSpend = args.GetProperty("subscriptionId").GetString();
                    var spend = await governanceService.GetCurrentSpendAsync(providerSpend!, subSpend!);
                    return $"The current spend for {providerSpend} ({subSpend}) is ${spend}.";

                case "forecast_monthly_spend":
                    var providerForecast = args.GetProperty("provider").GetString();
                    var subForecast = args.GetProperty("subscriptionId").GetString();
                    var forecast = await governanceService.ForecastMonthlySpendAsync(providerForecast!, subForecast!);
                    return $"The forecasted spend for {providerForecast} ({subForecast}) this month is ${forecast}.";

                case "stop_resource":
                    var resourceId = args.GetProperty("resourceId").GetString();
                    var providerStop = args.GetProperty("provider").GetString();
                    _logger.LogWarning("Blocked AI-initiated stop request for resource {ResourceId} on provider {Provider}. User approval is required.", resourceId, providerStop);
                    return $"Action proposal only: stopping resource {resourceId} on {providerStop} requires explicit user approval and was not executed.";

                default:
                    return "I'm sorry, that capability is not yet available in Draco. 🐉";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing tool call {Name}", name);
            return $"Error executing tool call: {ex.Message}";
        }
    }
}
