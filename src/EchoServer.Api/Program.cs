// Entry point: reads PORT env var, binds Kestrel to 0.0.0.0:PORT, exposes /healthz and echo endpoints.
using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using EchoServer.Api.Middleware;
using Microsoft.AspNetCore.Http.Extensions;

const int MaxBodyBytes = 10_485_760;

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
    // Allow reads past the cap so the handler can return HTTP 413 itself.
    options.Limits.MaxRequestBodySize = MaxBodyBytes + 1;
});

var app = builder.Build();

app.UseMiddleware<ControlHeaderMiddleware>();

app.MapGet("/healthz", () => Results.Content("OK", "text/plain"));

// GET /echo  — JSON metadata
app.MapGet("/echo", (HttpRequest req) => BuildMetadataResult(req));

// ANY /echo/metadata  — JSON metadata
app.Map("/echo/metadata", (HttpRequest req) => BuildMetadataResult(req));

// ANY /echo/body  — raw body echo; 204 if empty
app.Map("/echo/body", async (HttpRequest req, HttpResponse res) =>
{
    var (bytes, tooLarge) = await TryReadBodyAsync(req);
    if (tooLarge)
    {
        res.StatusCode = StatusCodes.Status413RequestEntityTooLarge;
        return;
    }
    if (bytes is null || bytes.Length == 0)
    {
        res.StatusCode = StatusCodes.Status204NoContent;
        return;
    }
    res.StatusCode = StatusCodes.Status200OK;
    if (req.ContentType is not null)
        res.ContentType = req.ContentType;
    await res.Body.WriteAsync(bytes);
});

// POST / PUT / PATCH / DELETE /echo  — raw body echo; fall back to metadata if empty
foreach (var method in new[] { "POST", "PUT", "PATCH", "DELETE" })
{
    app.MapMethods("/echo", new[] { method }, async (HttpRequest req, HttpResponse res) =>
    {
        var (bytes, tooLarge) = await TryReadBodyAsync(req);
        if (tooLarge)
        {
            res.StatusCode = StatusCodes.Status413RequestEntityTooLarge;
            return;
        }
        if (bytes is null || bytes.Length == 0)
        {
            var result = BuildMetadataResult(req);
            await result.ExecuteAsync(req.HttpContext);
            return;
        }
        res.StatusCode = StatusCodes.Status200OK;
        if (req.ContentType is not null)
            res.ContentType = req.ContentType;
        await res.Body.WriteAsync(bytes);
    });
}

app.Run();

static IResult BuildMetadataResult(HttpRequest req)
{
    var options = new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    var headers = new Dictionary<string, string[]>();
    foreach (var h in req.Headers)
        headers[h.Key] = h.Value.ToArray()!;

    var query = new Dictionary<string, string[]>();
    foreach (var q in req.Query)
        query[q.Key] = q.Value.ToArray()!;

    var metadata = new
    {
        method = req.Method,
        path = req.Path.Value,
        query,
        headers,
        protocol = req.Protocol,
        scheme = req.Scheme,
        host = req.Host.Value,
        url = req.GetDisplayUrl(),
    };

    var json = JsonSerializer.Serialize(metadata, options);
    return Results.Content(json, "application/json; charset=utf-8");
}

static async Task<(byte[]? Bytes, bool TooLarge)> TryReadBodyAsync(HttpRequest request)
{
    // Reject immediately when Content-Length already exceeds the cap.
    if (request.ContentLength.HasValue && request.ContentLength.Value > MaxBodyBytes)
        return (null, true);

    var buffer = new byte[81920];
    using var ms = new MemoryStream();
    int read;
    while ((read = await request.Body.ReadAsync(buffer, 0, buffer.Length)) > 0)
    {
        ms.Write(buffer, 0, read);
        if (ms.Length > MaxBodyBytes)
            return (null, true);
    }
    return ms.Length == 0 ? (null, false) : (ms.ToArray(), false);
}

public partial class Program { }
