FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY src/WebBoard.Agent/WebBoard.Agent.csproj src/WebBoard.Agent/
RUN dotnet restore src/WebBoard.Agent/WebBoard.Agent.csproj

COPY src/WebBoard.Agent/ src/WebBoard.Agent/
RUN dotnet publish src/WebBoard.Agent/WebBoard.Agent.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

USER root
RUN apt-get update \
    && apt-get install --yes --no-install-recommends systemd \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish .

USER $APP_UID
ENTRYPOINT ["dotnet", "WebBoard.Agent.dll"]
