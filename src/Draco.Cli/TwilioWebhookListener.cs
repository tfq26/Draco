using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Draco.Cli;

public class TwilioWebhookListener
{
    private readonly HttpListener _listener;
    private readonly ILogger<TwilioWebhookListener> _logger;
    private readonly Func<string, Task> _onMessageReceived;

    public TwilioWebhookListener(int port, Func<string, Task> onMessageReceived, ILogger<TwilioWebhookListener> logger)
    {
        _logger = logger;
        _onMessageReceived = onMessageReceived;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://*:{port}/webhook/twilio/");
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _listener.Start();
            _logger.LogInformation("Twilio Webhook Listener started on port {Port}.", _listener.Prefixes.First());

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
            
            _logger.LogDebug("Received webhook request: {Body}", body);

            // Twilio sends data as application/x-www-form-urlencoded
            var parameters = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(body);
            var messageResult = parameters["Body"].ToString();

            if (!string.IsNullOrEmpty(messageResult))
            {
                _logger.LogInformation("Incoming message received: {Message}", messageResult);
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
            _logger.LogError(ex, "Error processing webhook request.");
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.Close();
        }
    }
}
