FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY src/Webboard.Web/Webboard.Web.csproj src/Webboard.Web/
COPY src/Webboard.Domain/Webboard.Domain.csproj src/Webboard.Domain/
COPY src/Webboard.Domain.Model/Webboard.Domain.Model.csproj src/Webboard.Domain.Model/
COPY src/Webboard.Infrastructure/Webboard.Infrastructure.csproj src/Webboard.Infrastructure/
COPY src/Webboard.Infrastructure.Configuration/Webboard.Infrastructure.Configuration.csproj src/Webboard.Infrastructure.Configuration/
RUN dotnet restore src/Webboard.Web/Webboard.Web.csproj

COPY src/ src/
RUN dotnet publish src/Webboard.Web/Webboard.Web.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Webboard.Web.dll"]
