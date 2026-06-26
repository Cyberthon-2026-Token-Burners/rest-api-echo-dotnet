# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Highly performant `/echo` JSON metadata endpoint (GET) and `/echo/metadata` (ANY) mapping headers, query strings (with multiple values), paths, protocols, and methods.
- Raw payload echo capabilities under `/echo/body` (ANY) and `/echo` (POST/PUT/PATCH/DELETE), with content-type replication.
- Intelligent fallback for POST/PUT/PATCH/DELETE on `/echo` to return JSON metadata when the incoming request body is empty.
- Strict request body size ceiling protection (10,485,760 bytes limit) returning `HTTP 413 Payload Too Large` immediately upon violation.
- Robust memory-aware incremental reading of request streams to protect memory footprints and support latency constraints under 2.0ms for up to 1MB.
- UnsafeRelaxedJsonEscaping configuration in `System.Text.Json` to prevent double-escaping of non-ASCII characters in header and query values.
- Comprehensive usage guide detailing operational behaviors, raw body capabilities, and metadata representations.
- ControlHeaderMiddleware component intercepting `X-Echo-Status` and `X-Echo-Delay-ms` request headers.
- Asynchronous non-blocking delay capabilities for simulating customizable endpoint latencies.
- Rigid validation on control headers with precedence handling (checking HTTP Status codes [100, 599] before Delay parameters [0, 30000]).
- Pipeline integration registering middleware before routing elements inside `Program.cs`.
- Initial .NET 10 solution scaffold hosting `EchoServer.Api` and `EchoServer.Api.Tests`.
- Top-level ASP.NET Core Minimal API with dynamic `PORT` environment variable extraction and safe fallback configuration.
- Health check route `/healthz` delivering response body `"OK"`.
- Basic test harness integration verifying endpoint responsiveness.