# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- ControlHeaderMiddleware component intercepting `X-Echo-Status` and `X-Echo-Delay-ms` request headers.
- Asynchronous non-blocking delay capabilities for simulating customizable endpoint latencies.
- Rigid validation on control headers with precedence handling (checking HTTP Status codes [100, 599] before Delay parameters [0, 30000]).
- Pipeline integration registering middleware before routing elements inside `Program.cs`.
- Initial .NET 10 solution scaffold hosting `EchoServer.Api` and `EchoServer.Api.Tests`.
- Top-level ASP.NET Core Minimal API with dynamic `PORT` environment variable extraction and safe fallback configuration.
- Health check route `/healthz` delivering response body `"OK"`.
- Basic test harness integration verifying endpoint responsiveness.