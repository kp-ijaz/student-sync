# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish src/StudentSync.Api/StudentSync.Api.csproj -c Release -o /app --nologo

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /app/storage /app/keys && chown -R $APP_UID /app/storage /app/keys
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080 \
    DataProtection__KeysPath=/app/keys
EXPOSE 8080
ENTRYPOINT ["dotnet", "StudentSync.Api.dll"]
