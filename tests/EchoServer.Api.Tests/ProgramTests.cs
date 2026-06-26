using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EchoServer.Api
{
    [Collection("Sequential")]
    public class ProgramTests
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
    }
}
