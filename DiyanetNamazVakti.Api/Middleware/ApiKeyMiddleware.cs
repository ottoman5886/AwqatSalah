namespace DiyanetNamazVakti.Api.Middleware;

public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private const string API_KEY_HEADER = "X-API-Key";

    public ApiKeyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IConfiguration configuration)
    {
        if (context.Request.Path.StartsWithSegments("/swagger"))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(API_KEY_HEADER, out var extractedApiKey))
        {
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                "{\"success\":false,\"message\":\"API Key fehlt. Bitte X-API-Key Header setzen.\"}");
            return;
        }

        var apiKeys = configuration
            .GetSection("ApiKeys")
            .Get<string[]>() ?? Array.Empty<string>();

        if (!apiKeys.Contains(extractedApiKey.ToString()))
        {
            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                "{\"success\":false,\"message\":\"Ungültiger API Key.\"}");
            return;
        }

        await _next(context);
    }
}
