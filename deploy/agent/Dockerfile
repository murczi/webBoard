# Build from the repository root (the final dot is the build context):
# docker build -f deploy/agent/Dockerfile -t shujidev/webboard-agent:local .
# To deploy this image, set image: shujidev/webboard-agent:local in compose.yaml.
# Installation, runtime fields, and start/update commands: deploy/agent/README.md.
# Configure secrets and host mounts at runtime in Compose, never in this image.

# Build stage: the SDK restores dependencies and compiles the application.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Copy project files first so dependency restore can be cached across source edits.
COPY src/WebBoard.Agent/WebBoard.Agent.csproj src/WebBoard.Agent/
# Restore NuGet packages for the application and its project references.
RUN dotnet restore src/WebBoard.Agent/WebBoard.Agent.csproj

COPY src/WebBoard.Agent/ src/WebBoard.Agent/
# Publish Release output; reuse the restored packages and omit the SDK at runtime.
RUN dotnet publish src/WebBoard.Agent/WebBoard.Agent.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# Runtime stage: ASP.NET runtime plus published output only.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Install systemd/D-Bus tooling and utilities for host service integration.
USER root
RUN apt-get update \
    && apt-get install --yes --no-install-recommends systemd util-linux \
    && rm -rf /var/lib/apt/lists/*

# Default HTTP port; agent Compose overrides the listener with HTTPS on 8443.
ENV ASPNETCORE_HTTP_PORTS=8080
# Metadata only: Compose ports publishes a host port.
EXPOSE 8080

COPY --from=build /app/publish .

# Run as the non-root user provided by the .NET base image.
USER $APP_UID
# Start the agent API; runtime configuration comes from environment variables.
ENTRYPOINT ["dotnet", "WebBoard.Agent.dll"]
