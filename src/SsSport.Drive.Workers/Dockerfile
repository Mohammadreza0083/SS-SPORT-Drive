# syntax=docker/dockerfile:1
# Workers image (Hangfire jobs). Build context is the repository root:
#   docker build -f src/SsSport.Drive.Workers/Dockerfile .
# ffmpeg is added to the runtime stage when transcoding lands (EPIC 3).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the package layer is cached until a project file changes.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/SsSport.Drive.Domain/SsSport.Drive.Domain.csproj src/SsSport.Drive.Domain/
COPY src/SsSport.Drive.Application/SsSport.Drive.Application.csproj src/SsSport.Drive.Application/
COPY src/SsSport.Drive.Infrastructure/SsSport.Drive.Infrastructure.csproj src/SsSport.Drive.Infrastructure/
COPY src/SsSport.Drive.Workers/SsSport.Drive.Workers.csproj src/SsSport.Drive.Workers/
RUN dotnet restore src/SsSport.Drive.Workers/SsSport.Drive.Workers.csproj

COPY src/ src/
RUN dotnet publish src/SsSport.Drive.Workers/SsSport.Drive.Workers.csproj \
    --configuration Release --no-restore --output /app

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
USER $APP_UID
ENTRYPOINT ["dotnet", "SsSport.Drive.Workers.dll"]
