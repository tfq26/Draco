using System.Text.Json.Serialization;

namespace Draco.Infrastructure.AI;

public class ToolRegistry
{
    public static object GetToolDefinitions()
    {
        return new
        {
            function_declarations = new object[]
            {
                new
                {
                    name = "get_current_spend",
                    description = "Gets the current month-to-date spend for a specific cloud provider and subscription.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            provider = new { type = "string", description = "The cloud provider (Azure or AWS)" },
                            subscriptionId = new { type = "string", description = "The subscription or account ID" }
                        },
                        required = new[] { "provider", "subscriptionId" }
                    }
                },
                new
                {
                    name = "forecast_monthly_spend",
                    description = "Forecasts the total spend for the current month based on usage trends.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            provider = new { type = "string", description = "The cloud provider (Azure or AWS)" },
                            subscriptionId = new { type = "string", description = "The subscription or account ID" }
                        },
                        required = new[] { "provider", "subscriptionId" }
                    }
                },
                new
                {
                    name = "stop_resource",
                    description = "Stops or deallocates a high-cost cloud resource (e.g., VM, EC2 instance) to save costs.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            resourceId = new { type = "string", description = "The unique identifier of the resource to stop" },
                            provider = new { type = "string", description = "The cloud provider (Azure or AWS)" }
                        },
                        required = new[] { "resourceId", "provider" }
                    }
                }
            }
        };
    }
}
