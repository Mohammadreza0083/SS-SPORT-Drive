# syntax=docker/dockerfile:1
# API image. Build context is the repository root:
#   docker build -f src/SsSport.Drive.Api/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the package layer is cached until a project file changes.
COPY global.json nuget.config Directory.Build.props Directory.Packages.props ./
COPY src/SsSport.Drive.Domain/SsSport.Drive.Domain.csproj src/SsSport.Drive.Domain/
COPY src/SsSport.Drive.Application/SsSport.Drive.Application.csproj src/SsSport.Drive.Application/
COPY src/SsSport.Drive.Infrastructure/SsSport.Drive.Infrastructure.csproj src/SsSport.Drive.Infrastructure/
COPY src/SsSport.Drive.Api/SsSport.Drive.Api.csproj src/SsSport.Drive.Api/
RUN dotnet restore src/SsSport.Drive.Api/SsSport.Drive.Api.csproj

COPY src/ src/
RUN dotnet publish src/SsSport.Drive.Api/SsSport.Drive.Api.csproj \
    --configuration Release --no-restore --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID

# Plain-bash HTTP probe: the image ships no curl/wget, and adding packages is not worth the attack surface.
HEALTHCHECK --interval=10s --timeout=3s --start-period=20s --retries=5 \
    CMD bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080 \
        && printf "GET /health/live HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n" >&3 \
        && head -n 1 <&3 | grep -q " 200 "'

ENTRYPOINT ["dotnet", "SsSport.Drive.Api.dll"]
