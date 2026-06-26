FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# Copy solution and project files
COPY EchoServer.sln .
COPY src/EchoServer.Api/EchoServer.Api.csproj src/EchoServer.Api/
COPY tests/EchoServer.Api.Tests/EchoServer.Api.Tests.csproj tests/EchoServer.Api.Tests/

# Restore dependencies
RUN dotnet restore

# Copy remaining code and publish
COPY . .
RUN dotnet publish src/EchoServer.Api/EchoServer.Api.csproj -c Release -o /app/publish --no-restore

# Production Stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Use existing non-root user 'app' defined in standard dotnet aspnet runtime image
USER app

# The application reads PORT env var dynamically; we nullify default ASPNETCORE_URLS to let it bind explicitly
ENV ASPNETCORE_URLS=""

ENTRYPOINT ["dotnet", "EchoServer.Api.dll"]