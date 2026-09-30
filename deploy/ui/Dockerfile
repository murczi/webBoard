# Build from the repository root (the final dot is the build context):
# docker build -f deploy/ui/Dockerfile -t shujidev/webboard:local .
# To deploy this image, set image: shujidev/webboard:local in compose.yaml.
# Installation, runtime fields, and start/update commands: deploy/ui/README.md.
# Configure secrets and host mounts at runtime in Compose, never in this image.

# Build stage: the SDK restores dependencies and compiles the application.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Copy project files first so dependency restore can be cached across source edits.
COPY src/Webboard.Web/Webboard.Web.csproj src/Webboard.Web/
COPY src/Webboard.Domain/Webboard.Domain.csproj src/Webboard.Domain/
COPY src/Webboard.Domain.Model/Webboard.Domain.Model.csproj src/Webboard.Domain.Model/
COPY src/Webboard.Infrastructure/Webboard.Infrastructure.csproj src/Webboard.Infrastructure/
COPY src/Webboard.Infrastructure.Configuration/Webboard.Infrastructure.Configuration.csproj src/Webboard.Infrastructure.Configuration/
# Restore NuGet packages for the application and its project references.
RUN dotnet restore src/Webboard.Web/Webboard.Web.csproj

COPY src/ src/
# Publish Release output; reuse the restored packages and omit the SDK at runtime.
RUN dotnet publish src/Webboard.Web/Webboard.Web.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# Runtime stage: ASP.NET runtime plus published output only.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
# Default internal HTTP port; the HTTPS reverse proxy connects here.
ENV ASPNETCORE_HTTP_PORTS=8080
# Metadata only: Compose ports publishes a host port.
EXPOSE 8080
COPY --from=build /app/publish .
# Run as the non-root user provided by the .NET base image.
USER $APP_UID
# Compose run appends arguments, such as --migrate for the UI.
ENTRYPOINT ["dotnet", "Webboard.Web.dll"]
