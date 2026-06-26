using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using EchoServer.Api.Middleware;

namespace EchoServer.Api
{
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
    }
}
