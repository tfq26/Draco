namespace Draco.Api.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", DashboardEndpoints.GetHealthAsync)
            .WithName("ApiHealthCheck");
    }
}
