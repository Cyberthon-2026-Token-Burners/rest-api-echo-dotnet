// Entry point: reads PORT env var, binds Kestrel to 0.0.0.0:PORT, exposes GET /healthz.
using System.Net;
using EchoServer.Api.Middleware;

var port = 8080;
var portEnv = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(portEnv)
    && int.TryParse(portEnv, out var parsed)
    && parsed >= 1
    && parsed <= 65535)
{
    port = parsed;
}

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(IPAddress.Any, port);
});

var app = builder.Build();

app.UseMiddleware<ControlHeaderMiddleware>();

app.MapGet("/healthz", () => Results.Content("OK", "text/plain"));

app.Run();

public partial class Program { }
