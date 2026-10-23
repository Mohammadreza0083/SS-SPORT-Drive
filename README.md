# SS-SPORT Drive

Branded, trackable volleyball player video pages built from Google Drive folders, for the SS-SPORT agency.

## Layout

| Path | What |
|---|---|
| `src/SsSport.Drive.Domain` | Entities, aggregates, value objects, domain events |
| `src/SsSport.Drive.Application` | MediatR commands and queries, DTOs, specifications |
| `src/SsSport.Drive.Infrastructure` | EF Core, Unit of Work, repositories, external services |
| `src/SsSport.Drive.Api` | HTTP API host (OpenAPI + Scalar) |
| `src/SsSport.Drive.Workers` | Background jobs host (Hangfire) |
| `tests/` | Unit, architecture and integration tests (xUnit v3, Testcontainers) |
| `web/` | Next.js front-end |
| `edge/` | Cloudflare Worker |
| `deploy/` | Docker Compose, deployment and operations |

## Prerequisites

- .NET SDK 10 (see `global.json`)
- Node.js 24 (see `.nvmrc`)
- Docker (integration tests start PostgreSQL with Testcontainers)

## Build and test

```bash
dotnet build SsSport.Drive.slnx
dotnet test --solution SsSport.Drive.slnx
```

See `web/README.md` and `edge/README.md` for the front-end and the worker.

## Conventions

- One branch per feature: `feature/<area>-<short-name>`, merged into `main` through a pull request with green CI.
- Code, comments, logs and commits are in English. XML docs on all .NET members are enforced by the build.
- NuGet versions live only in `Directory.Packages.props`.
