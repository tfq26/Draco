using System.Text;
using System.Text.Json;
using Draco.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Draco.Infrastructure.Services;

public class GeminiAIService : IAIService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GeminiAIService> _logger;
    private readonly string _apiKey;
    private const string ApiUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-3-flash-preview:generateContent";

    public GeminiAIService(HttpClient httpClient, ILogger<GeminiAIService> logger, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;
        _apiKey = configuration["Gemini:ApiKey"] ?? configuration["GOOGLE_GEMINI_API_KEY"] ?? "KEY";
    }

    public async Task<string> AnalyzeAnomalyAsync(string rawData, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Analyzing cloud data with Gemini...");
        var prompt = $"Analyze the following cloud resource data and identify any cost anomalies or security risks. Redact any PII. Data: {rawData}";
        return await CallGeminiAsync(prompt, cancellationToken);
    }

    public async Task<string> GenerateRemediationHclAsync(string context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating Terraform remediation for context: {Context}", context);
        var prompt = $"Given this cloud anomaly: {context}, generate a valid and safe Terraform HCL snippet to remediate the issue. Output ONLY the HCL.";
        return await CallGeminiAsync(prompt, cancellationToken);
    }

    public async Task<string> CreateConversationalAlertAsync(string analysis, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating natural language alert for analysis.");
        var prompt = $"Translate this technical cloud analysis into a concise, professional message for a sentinel to send via SMS: {analysis}";
        return await CallGeminiAsync(prompt, cancellationToken);
    }

    public async Task<string> ProcessQueryAsync(string query, string context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing user query: {Query}", query);
        var prompt = $@"You are Draco 🐉, a friendly and helpful autonomous cloud governance AI. 
The user is asking: '{query}'
Here is the context of their cloud infrastructure: {context}

INSTRUCTIONS:
1. Answer the user in a warm, professional, and friendly tone.
2. Use emojis naturally throughout your response to make it feel alive! 🚀
3. DO NOT use Markdown (no asterisks, no deep headers, no backticks). Use plain text only.
4. If they ask for cost/billing, look for clues and provide a friendly estimate or advice.
5. Keep it concise but helpful.";

        return await CallGeminiAsync(prompt, cancellationToken);
    }

    private async Task<string> CallGeminiAsync(string prompt, CancellationToken cancellationToken)
    {
        var requestBody = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } }
            }
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
        
        var candidates = doc.RootElement.GetProperty("candidates");
        if (candidates.GetArrayLength() > 0)
        {
            var parts = candidates[0].GetProperty("content").GetProperty("parts");
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var textElement))
                {
                    return textElement.GetString() ?? "No analysis provided.";
                }
            }
        }

        return "No analysis provided.";
    }
}
