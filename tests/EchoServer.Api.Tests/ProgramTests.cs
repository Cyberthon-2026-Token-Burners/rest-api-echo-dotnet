using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using EchoServer.Api.Middleware;

[Collection("Sequential")]
public class ProgramTests
{
    private static readonly object _lock = new object();

    private sealed class FakeHttpResponseFeature : HttpResponseFeature
    {
        private readonly List<Func<object, Task>> _callbacks = new();
        private readonly List<object> _states = new();

        public override void OnStarting(Func<object, Task> callback, object state)
        {
            _callbacks.Add(callback);
            _states.Add(state);
        }

        public async Task FireOnStartingAsync()
        {
            for (int i = 0; i < _callbacks.Count; i++)
            {
                await _callbacks[i](_states[i]);
            }
        }
    }

    private sealed class RepeatingStream : Stream
    {
        private readonly long _length;
        private long _position;

        public RepeatingStream(long length)
        {
            _length = length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _length) return 0;
            int remaining = (int)Math.Min(count, _length - _position);
            Array.Fill(buffer, (byte)120, offset, remaining);
            _position += remaining;
            return remaining;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (_position >= _length) return 0;
            int remaining = (int)Math.Min(count, _length - _position);
            Array.Fill(buffer, (byte)120, offset, remaining);
            _position += remaining;
            return await Task.FromResult(remaining);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position >= _length) return 0;
            int remaining = (int)Math.Min(buffer.Length, _length - _position);
            buffer.Span.Slice(0, remaining).Fill((byte)120);
            _position += remaining;
            return await ValueTask.FromResult(remaining);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }
    }

    private sealed class RequestMetadata
    {
        public string Method { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Protocol { get; set; } = string.Empty;
        public Dictionary<string, string[]> Headers { get; set; } = new();
        public Dictionary<string, string[]> Query { get; set; } = new();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("9095")]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("80")]
    [InlineData("65535")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Test_Healthz_Endpoint_With_Various_Ports(string? portValue)
    {
        lock (_lock)
        {
            Environment.SetEnvironmentVariable("PORT", portValue);
        }

        try
        {
            using var factory = new WebApplicationFactory<Program>();
            using var client = factory.CreateClient();
            using var response = await client.GetAsync("/healthz");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var content = await response.Content.ReadAsStringAsync();
            Assert.Equal("OK", content);
            Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        }
        finally
        {
            lock (_lock)
            {
                Environment.SetEnvironmentVariable("PORT", null);
            }
        }
    }

    [Theory]
    [InlineData("418", "150", 418, true)]
    [InlineData("200", "0", 200, true)]
    [InlineData(null, null, 200, true)]
    [InlineData("99", null, 400, false)]
    [InlineData("invalid", null, 400, false)]
    [InlineData(null, "-5", 400, false)]
    [InlineData(null, "35000", 400, false)]
    [InlineData("99", "-5", 400, false)]
    [InlineData("   ", null, 400, false)]
    [InlineData(null, "   ", 400, false)]
    [InlineData("", null, 400, false)]
    [InlineData(null, "", 400, false)]
    [InlineData("418 ", null, 400, false)]
    [InlineData(" 418", null, 400, false)]
    [InlineData("2147483648", null, 400, false)]
    public async Task Test_ControlHeaderMiddleware_Unit(
        string? statusHeader,
        string? delayHeader,
        int expectedStatus,
        bool expectedNextCalled)
    {
        var context = new DefaultHttpContext();
        var fakeFeature = new FakeHttpResponseFeature();
        context.Features.Set<IHttpResponseFeature>(fakeFeature);

        if (statusHeader != null)
        {
            context.Request.Headers["X-Echo-Status"] = statusHeader;
        }
        if (delayHeader != null)
        {
            context.Request.Headers["X-Echo-Delay-ms"] = delayHeader;
        }

        using var responseStream = new MemoryStream();
        context.Response.Body = responseStream;

        bool nextCalled = false;
        RequestDelegate next = async ctx =>
        {
            nextCalled = true;
            ctx.Response.StatusCode = 200;
            await fakeFeature.FireOnStartingAsync();
            await ctx.Response.WriteAsync("PipelineOK");
        };

        var middleware = new ControlHeaderMiddleware(next);
        await middleware.InvokeAsync(context);

        Assert.Equal(expectedNextCalled, nextCalled);
        Assert.Equal(expectedStatus, context.Response.StatusCode);

        responseStream.Position = 0;
        using var reader = new StreamReader(responseStream);
        string body = await reader.ReadToEndAsync();

        if (!expectedNextCalled)
        {
            if (statusHeader == "99" || statusHeader == "invalid" || statusHeader == "" || statusHeader == "   " || statusHeader == "418 " || statusHeader == " 418" || statusHeader == "2147483648")
            {
                Assert.Contains("X-Echo-Status", body);
            }
            else if (delayHeader == "-5" || delayHeader == "35000" || delayHeader == "" || delayHeader == "   ")
            {
                Assert.Contains("X-Echo-Delay-ms", body);
            }
        }
    }

    [Theory]
    [InlineData(null, null, HttpStatusCode.OK, "OK")]
    [InlineData("418", "50", (HttpStatusCode)418, "OK")]
    [InlineData("99", null, HttpStatusCode.BadRequest, "X-Echo-Status")]
    [InlineData("invalid", null, HttpStatusCode.BadRequest, "X-Echo-Status")]
    [InlineData(null, "-5", HttpStatusCode.BadRequest, "X-Echo-Delay-ms")]
    [InlineData(null, "35000", HttpStatusCode.BadRequest, "X-Echo-Delay-ms")]
    [InlineData("99", "-5", HttpStatusCode.BadRequest, "X-Echo-Status")]
    public async Task Test_Program_Integration_ControlHeaders(
        string? statusHeader,
        string? delayHeader,
        HttpStatusCode expectedStatus,
        string expectedBodyPart)
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/healthz");
        if (statusHeader != null)
        {
            request.Headers.TryAddWithoutValidation("X-Echo-Status", statusHeader);
        }
        if (delayHeader != null)
        {
            request.Headers.TryAddWithoutValidation("X-Echo-Delay-ms", delayHeader);
        }

        using var response = await client.SendAsync(request);

        Assert.Equal(expectedStatus, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains(expectedBodyPart, content);
    }

    [Fact]
    public async Task Test_Echo_Metadata_Get_With_Query_And_Headers()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/echo?p=1&p=2");
        request.Headers.Add("X-Test", "Sample");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var bodyString = await response.Content.ReadAsStringAsync();
        var metadata = JsonSerializer.Deserialize<RequestMetadata>(bodyString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(metadata);
        Assert.Equal("GET", metadata.Method);
        Assert.Equal("/echo", metadata.Path);
        Assert.Contains("HTTP", metadata.Protocol);

        Assert.True(metadata.Headers.ContainsKey("X-Test"));
        Assert.Contains("Sample", metadata.Headers["X-Test"]);

        Assert.True(metadata.Query.ContainsKey("p"));
        Assert.Equal(new[] { "1", "2" }, metadata.Query["p"]);
    }

    [Fact]
    public async Task Test_Echo_Body_Post_Raw_Bytes()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        byte[] payload = new byte[1024];
        Random.Shared.NextBytes(payload);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/echo");
        request.Content = new ByteArrayContent(payload);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);

        byte[] responseBytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(payload, responseBytes);
    }

    [Fact]
    public async Task Test_Echo_Body_Empty_Returns_NoContent()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/echo/body");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Test_Echo_Payload_Too_Large_ContentLength()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/echo");
        var stream = new RepeatingStream(10485761);
        var content = new StreamContent(stream);
        content.Headers.ContentLength = 10485761;
        request.Content = content;

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Test_Echo_Payload_Too_Large_Chunked()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/echo");
        var stream = new RepeatingStream(10485761);
        var content = new StreamContent(stream);
        request.Content = content;

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Test_Echo_Payload_Boundary_Size_ContentLength()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/echo");
        var stream = new RepeatingStream(10485760);
        var content = new StreamContent(stream);
        content.Headers.ContentLength = 10485760;
        request.Content = content;

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var responseStream = await response.Content.ReadAsStreamAsync();
        byte[] buffer = new byte[8192];
        long totalRead = 0;
        int read;
        while ((read = await responseStream.ReadAsync(buffer)) > 0)
        {
            totalRead += read;
        }
        Assert.Equal(10485760, totalRead);
    }

    [Fact]
    public async Task Test_Echo_Payload_Boundary_Size_Chunked()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/echo");
        var stream = new RepeatingStream(10485760);
        var content = new StreamContent(stream);
        request.Content = content;

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var responseStream = await response.Content.ReadAsStreamAsync();
        byte[] buffer = new byte[8192];
        long totalRead = 0;
        int read;
        while ((read = await responseStream.ReadAsync(buffer)) > 0)
        {
            totalRead += read;
        }
        Assert.Equal(10485760, totalRead);
    }

    [Fact]
    public async Task Test_Echo_Fallback_To_Metadata_On_Empty_Body()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/echo");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var bodyString = await response.Content.ReadAsStringAsync();
        var metadata = JsonSerializer.Deserialize<RequestMetadata>(bodyString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(metadata);
        Assert.Equal("POST", metadata.Method);
        Assert.Equal("/echo", metadata.Path);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public async Task Test_Echo_Metadata_Various_Methods(string method)
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(new HttpMethod(method), "/echo/metadata");
        if (method != "GET")
        {
            request.Content = new StringContent("some body payload");
        }

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var bodyString = await response.Content.ReadAsStringAsync();
        var metadata = JsonSerializer.Deserialize<RequestMetadata>(bodyString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(metadata);
        Assert.Equal(method, metadata.Method);
        Assert.Equal("/echo/metadata", metadata.Path);
    }

    [Theory]
    [InlineData("POST", "Hello Body", HttpStatusCode.OK, "Hello Body")]
    [InlineData("PUT", "Test Data", HttpStatusCode.OK, "Test Data")]
    [InlineData("PATCH", "Patch Data", HttpStatusCode.OK, "Patch Data")]
    [InlineData("POST", "", HttpStatusCode.NoContent, "")]
    [InlineData("PUT", "", HttpStatusCode.NoContent, "")]
    public async Task Test_Echo_Body_Various_Methods(string method, string sendBody, HttpStatusCode expectedStatus, string expectedResponseBody)
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(new HttpMethod(method), "/echo/body");
        if (sendBody != "")
        {
            request.Content = new StringContent(sendBody);
        }

        using var response = await client.SendAsync(request);

        Assert.Equal(expectedStatus, response.StatusCode);
        if (expectedStatus == HttpStatusCode.OK)
        {
            var responseBody = await response.Content.ReadAsStringAsync();
            Assert.Equal(expectedResponseBody, responseBody);
        }
    }

    [Fact]
    public async Task Test_Echo_Body_Get_Returns_NoContent()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/echo/body");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
