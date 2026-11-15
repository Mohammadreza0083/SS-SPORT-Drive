# syntax=docker/dockerfile:1
# Workers image (Hangfire jobs). Build context is the repository root:
#   docker build -f src/SsSport.Drive.Workers/Dockerfile .
# The runtime stage ships ffmpeg and ffprobe for transcoding (Epic 3).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the package layer is cached until a project file changes.
COPY global.json nuget.config Directory.Build.props Directory.Packages.props ./
COPY src/SsSport.Drive.Domain/SsSport.Drive.Domain.csproj src/SsSport.Drive.Domain/
COPY src/SsSport.Drive.Application/SsSport.Drive.Application.csproj src/SsSport.Drive.Application/
COPY src/SsSport.Drive.Infrastructure/SsSport.Drive.Infrastructure.csproj src/SsSport.Drive.Infrastructure/
COPY src/SsSport.Drive.Workers/SsSport.Drive.Workers.csproj src/SsSport.Drive.Workers/
RUN dotnet restore src/SsSport.Drive.Workers/SsSport.Drive.Workers.csproj

COPY src/ src/
RUN dotnet publish src/SsSport.Drive.Workers/SsSport.Drive.Workers.csproj \
    --configuration Release --no-restore --output /app

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
RUN apt-get update     && apt-get install -y --no-install-recommends ffmpeg     && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./
# Scratch space for ingest (Media__WorkPath). It must belong to the app user: an empty named volume mounted
# here takes this folder's owner, and the jobs run as $APP_UID, not root.
RUN mkdir -p /work && chown "$APP_UID:$APP_UID" /work
USER $APP_UID
ENTRYPOINT ["dotnet", "SsSport.Drive.Workers.dll"]
