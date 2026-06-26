using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using EchoServer.Api.Middleware;

namespace EchoServer.Api.Middleware
{
    public class ControlHeaderMiddlewareTests
    {
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

        [Fact]
        public async Task InvokeAsync_NoControlHeaders_PassesThrough()
        {
            var context = new DefaultHttpContext();
            var fakeFeature = new FakeHttpResponseFeature();
            context.Features.Set<IHttpResponseFeature>(fakeFeature);
            var responseStream = new MemoryStream();
            context.Response.Body = responseStream;
            context.Response.StatusCode = 200;

            bool nextCalled = false;
            RequestDelegate next = async ctx =>
            {
                nextCalled = true;
                await fakeFeature.FireOnStartingAsync();
            };

            var middleware = new ControlHeaderMiddleware(next);
            await middleware.InvokeAsync(context);

            Assert.True(nextCalled);
            Assert.Equal(200, context.Response.StatusCode);
        }

        [Theory]
        [InlineData("100", 0)]
        [InlineData("200", 10)]
        [InlineData("418", 150)]
        [InlineData("500", 50)]
        [InlineData("599", 5)]
        public async Task InvokeAsync_ValidControlHeaders_AppliesStatusAndDelay(string statusHeader, int delayMs)
        {
            var context = new DefaultHttpContext();
            var fakeFeature = new FakeHttpResponseFeature();
            context.Features.Set<IHttpResponseFeature>(fakeFeature);
            var responseStream = new MemoryStream();
            context.Response.Body = responseStream;
            context.Response.StatusCode = 200;

            context.Request.Headers["X-Echo-Status"] = statusHeader;
            context.Request.Headers["X-Echo-Delay-ms"] = delayMs.ToString();

            bool nextCalled = false;
            RequestDelegate next = async ctx =>
            {
                nextCalled = true;
                await fakeFeature.FireOnStartingAsync();
            };

            var middleware = new ControlHeaderMiddleware(next);
            var stopwatch = Stopwatch.StartNew();
            await middleware.InvokeAsync(context);
            stopwatch.Stop();

            Assert.True(nextCalled);
            Assert.Equal(int.Parse(statusHeader), context.Response.StatusCode);
            Assert.True(stopwatch.ElapsedMilliseconds >= 0);
        }

        [Theory]
        [InlineData("99")]
        [InlineData("1000")]
        [InlineData("0")]
        [InlineData("-1")]
        [InlineData("invalid")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("123a")]
        [InlineData("2147483648")]
        [InlineData("-2147483649")]
        public async Task InvokeAsync_InvalidStatus_ShortCircuitsWith400(string statusHeader)
        {
            var context = new DefaultHttpContext();
            var fakeFeature = new FakeHttpResponseFeature();
            context.Features.Set<IHttpResponseFeature>(fakeFeature);
            var responseStream = new MemoryStream();
            context.Response.Body = responseStream;
            context.Response.StatusCode = 200;

            context.Request.Headers["X-Echo-Status"] = statusHeader;

            bool nextCalled = false;
            RequestDelegate next = async ctx =>
            {
                nextCalled = true;
                await fakeFeature.FireOnStartingAsync();
            };

            var middleware = new ControlHeaderMiddleware(next);
            await middleware.InvokeAsync(context);

            Assert.False(nextCalled);
            Assert.Equal(400, context.Response.StatusCode);

            responseStream.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(responseStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            Assert.Contains("X-Echo-Status", body);
            Assert.Contains("Invalid", body);
        }

        [Theory]
        [InlineData("-1")]
        [InlineData("-5")]
        [InlineData("30001")]
        [InlineData("35000")]
        [InlineData("invalid")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("123a")]
        [InlineData("2147483648")]
        public async Task InvokeAsync_InvalidDelay_ShortCircuitsWith400(string delayHeader)
        {
            var context = new DefaultHttpContext();
            var fakeFeature = new FakeHttpResponseFeature();
            context.Features.Set<IHttpResponseFeature>(fakeFeature);
            var responseStream = new MemoryStream();
            context.Response.Body = responseStream;
            context.Response.StatusCode = 200;

            context.Request.Headers["X-Echo-Delay-ms"] = delayHeader;

            bool nextCalled = false;
            RequestDelegate next = async ctx =>
            {
                nextCalled = true;
                await fakeFeature.FireOnStartingAsync();
            };

            var middleware = new ControlHeaderMiddleware(next);
            await middleware.InvokeAsync(context);

            Assert.False(nextCalled);
            Assert.Equal(400, context.Response.StatusCode);

            responseStream.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(responseStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            Assert.Contains("X-Echo-Delay-ms", body);
            Assert.Contains("Invalid", body);
        }

        [Theory]
        [InlineData("99", "-5")]
        [InlineData("invalid", "35000")]
        [InlineData("", "invalid")]
        [InlineData("1000", "10")]
        [InlineData("-100", "-1")]
        public async Task InvokeAsync_BothInvalid_StatusValidationTakesPrecedence(string statusHeader, string delayHeader)
        {
            var context = new DefaultHttpContext();
            var fakeFeature = new FakeHttpResponseFeature();
            context.Features.Set<IHttpResponseFeature>(fakeFeature);
            var responseStream = new MemoryStream();
            context.Response.Body = responseStream;
            context.Response.StatusCode = 200;

            context.Request.Headers["X-Echo-Status"] = statusHeader;
            context.Request.Headers["X-Echo-Delay-ms"] = delayHeader;

            bool nextCalled = false;
            RequestDelegate next = async ctx =>
            {
                nextCalled = true;
                await fakeFeature.FireOnStartingAsync();
            };

            var middleware = new ControlHeaderMiddleware(next);
            await middleware.InvokeAsync(context);

            Assert.False(nextCalled);
            Assert.Equal(400, context.Response.StatusCode);

            responseStream.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(responseStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            Assert.Contains("X-Echo-Status", body);
            Assert.Contains("Invalid", body);
        }
    }

    [Collection("Sequential")]
    public class ProgramIntegrationTests
    {
        private static readonly object _lock = new object();

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
    }
}
