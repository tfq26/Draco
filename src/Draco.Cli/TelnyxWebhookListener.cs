using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Draco.Cli;

public class TelnyxWebhookListener
{
    private readonly HttpListener _listener;
    private readonly ILogger<TelnyxWebhookListener> _logger;
    private readonly Func<string, Task> _onMessageReceived;

    public TelnyxWebhookListener(int port, Func<string, Task> onMessageReceived, ILogger<TelnyxWebhookListener> logger)
    {
        _logger = logger;
        _onMessageReceived = onMessageReceived;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://*:{port}/webhook/telnyx/");
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _listener.Start();
            _logger.LogInformation("Telnyx Webhook Listener started on {Prefix}.", _listener.Prefixes.First());

            while (!cancellationToken.IsCancellationRequested)
            {
                var context = await _listener.GetContextAsync();
                _ = ProcessRequestAsync(context); // Fire and forget
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Webhook listener encountered an error.");
        }
        finally
        {
            _listener.Close();
        }
    }

    private async Task ProcessRequestAsync(HttpListenerContext context)
    {
        try
        {
            using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
            var body = await reader.ReadToEndAsync();

            _logger.LogDebug("Received Telnyx webhook request: {Body}", body);

            // Telnyx V2 Webhook Payload Structure
            // { "data": { "payload": { "text": "..." } } }
            var messageResult = ExtractMessageBody(body);

            if (!string.IsNullOrEmpty(messageResult))
            {
                _logger.LogInformation("Incoming message received from Telnyx: {Message}", messageResult);
                await _onMessageReceived(messageResult);
            }

            context.Response.StatusCode = (int)HttpStatusCode.OK;
            var responseText = "OK";
            var buffer = Encoding.UTF8.GetBytes(responseText);
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            context.Response.OutputStream.Close();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Telnyx webhook request.");
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.Close();
        }
    }

    private static string? ExtractMessageBody(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.TryGetProperty("data", out var data) && 
                data.TryGetProperty("payload", out var payload) &&
                payload.TryGetProperty("text", out var text))
            {
                return text.GetString();
            }

            // Fallback
            if (root.TryGetProperty("text", out var simpleText)) return simpleText.GetString();
        }
        catch
        {
            // Ignore parse issues
        }

        return null;
    }
}
