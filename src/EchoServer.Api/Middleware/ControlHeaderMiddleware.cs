// Middleware that intercepts X-Echo-Status and X-Echo-Delay-ms request headers to allow
// clients to control the response status code and introduce artificial delays.
namespace EchoServer.Api.Middleware;

public class ControlHeaderMiddleware
{
    private const string StatusHeader = "X-Echo-Status";
    private const string DelayHeader = "X-Echo-Delay-ms";
    private const int MaxDelayMs = 30_000;

    private readonly RequestDelegate _next;

    public ControlHeaderMiddleware(RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(next);
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        int? statusCode = null;
        int? delayMs = null;

        if (context.Request.Headers.TryGetValue(StatusHeader, out var statusValues))
        {
            var raw = statusValues.ToString();
            if (!int.TryParse(raw, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                || parsed < 100 || parsed > 599)
            {
                context.Response.StatusCode = 400;
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync("Invalid X-Echo-Status value.");
                return;
            }
            statusCode = parsed;
        }

        if (context.Request.Headers.TryGetValue(DelayHeader, out var delayValues))
        {
            var raw = delayValues.ToString();
            if (!int.TryParse(raw, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                || parsed < 0 || parsed > MaxDelayMs)
            {
                context.Response.StatusCode = 400;
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync("Invalid X-Echo-Delay-ms value.");
                return;
            }
            delayMs = parsed;
        }

        if (delayMs.HasValue)
        {
            await Task.Delay(delayMs.Value);
        }

        if (statusCode.HasValue)
        {
            context.Response.StatusCode = statusCode.Value;
            context.Response.OnStarting(state =>
            {
                var response = (HttpResponse)state;
                response.StatusCode = statusCode.Value;
                return Task.CompletedTask;
            }, context.Response);
        }

        await _next(context);
    }
}
