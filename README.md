<div align="center">

# SS-SPORT Drive

**Fetch Your Dreams.**

Branded, trackable volleyball player video pages, built straight from Google Drive.

[![CI](https://github.com/Mohammadreza0083/SS-SPORT-Drive/actions/workflows/ci.yml/badge.svg)](https://github.com/Mohammadreza0083/SS-SPORT-Drive/actions/workflows/ci.yml)
![.NET 10](https://img.shields.io/badge/.NET-10-CC0000?logo=dotnet&logoColor=white)
![Next.js 16](https://img.shields.io/badge/Next.js-16-07080B?logo=nextdotjs&logoColor=white)
![PostgreSQL 17](https://img.shields.io/badge/PostgreSQL-17-660000?logo=postgresql&logoColor=white)
![Cloudflare](https://img.shields.io/badge/Edge-Cloudflare-FF4545?logo=cloudflare&logoColor=white)
![Docker](https://img.shields.io/badge/Runs%20in-Docker-1E222B?logo=docker&logoColor=white)
![License](https://img.shields.io/badge/License-Proprietary-8A93A6)

[What it does](#what-it-does) ·
[Architecture](#architecture) ·
[Tech stack](#tech-stack) ·
[Quick start](#quick-start) ·
[Project structure](#project-structure) ·
[Engineering rules](#engineering-rules) ·
[Roadmap](#roadmap)

</div>

---

## What it does

SS-SPORT analysts drop a player's footage into a Google Drive folder named `SS-SPORT <Full Name>`. SS-SPORT Drive turns it into a polished, brand-styled player page and tracks who watches it.

| | |
|---|---|
| 🎬 **Ingest** | Reads Drive folders and subfolders, copies originals to Cloudflare R2, transcodes to HLS (360p / 720p / 1080p) with the burned-in SS-SPORT watermark. |
| 🔗 **Personal links** | Every club, scout or agent gets their own `/r/{code}` link, gated by email OTP, with device binding and forwarding alerts. |
| 🛡️ **Protection** | HLS segments only, 30-minute HMAC-signed tokens refreshed while watching, a moving viewer-email overlay. No downloads, ever. |
| 📊 **Tracking** | Plays, minutes watched, completion, replay hotspots and last-seen time per viewer, per player and per video. |
| 🔎 **Public search** | Anyone can search players. Media stays blurred and locked until an admin approves an access request. |
| 🤖 **Telegram** | Analysts add players from a Telegram Mini App; admins get approvals, alerts and a daily digest from the bot. |
| 🧭 **Admin console** | Dashboard, waiting list, players, links, viewers, access requests, sync jobs and the email queue. |

## Architecture

```mermaid
flowchart LR
    classDef edge fill:#CC0000,stroke:#660000,color:#F2F4F7
    classDef app fill:#0F1117,stroke:#1E222B,color:#F2F4F7
    classDef ext fill:#1E222B,stroke:#8A93A6,color:#F2F4F7

    Viewer([Club / scout / agent]) --> Edge
    Admin([Admin]) --> Web
    Analyst([Analyst]) --> TG[Telegram bot & Mini App]:::ext

    subgraph Cloudflare
        Edge[Edge Worker<br/>short links · HLS tokens]:::edge
        R2[(R2<br/>originals · HLS)]:::edge
    end

    subgraph VPS["VPS (Docker)"]
        Web[Next.js web]:::app
        Api[ASP.NET Core API<br/>MediatR · CQRS]:::app
        Workers[Hangfire workers<br/>ingest · ffmpeg · sync · email]:::app
        Db[(PostgreSQL)]:::app
    end

    Edge --> Web
    Edge --> R2
    Web --> Api
    TG --> Api
    Api --> Db
    Workers --> Db
    Workers --> R2
    Workers --> Drive[(Google Drive)]:::ext
    Workers --> Mail[Resend · Brevo · Google SMTP]:::ext
```

The backend follows **Clean Architecture** with **CQRS + DDD**. Dependencies point inwards only, and architecture tests enforce it:

```
Api / Workers  →  Infrastructure  →  Application  →  Domain
```

## Tech stack

| Area | Technology |
|---|---|
| Backend | .NET 10, ASP.NET Core, MediatR, AutoMapper, FluentValidation, EF Core |
| Data | PostgreSQL, Unit of Work + generic repository, Specification pattern, `AsNoTracking` read projections |
| Jobs | Hangfire (PostgreSQL storage), ffmpeg |
| Front-end | Next.js 16, TypeScript, Media Chrome + hls-video-element |
| Edge & storage | Cloudflare Workers, Cloudflare R2, Turnstile |
| API docs | OpenAPI + Scalar at `/scalar` |
| Observability | Serilog → Grafana Loki → Grafana |
| Testing | xUnit v3, FluentAssertions, NSubstitute, NetArchTest, Testcontainers, Vitest, Playwright, k6 |
| Delivery | Docker, GitHub Actions |

## Quick start

**Prerequisites:** [.NET SDK 10](https://dotnet.microsoft.com/download) · [Node.js 24](https://nodejs.org) · [Docker](https://www.docker.com/products/docker-desktop/) (integration tests start PostgreSQL with Testcontainers)

```bash
# Backend: build and run every test (unit, architecture, integration)
dotnet build SsSport.Drive.slnx
dotnet test --solution SsSport.Drive.slnx

# Web
cd web && npm ci && npm run dev        # http://localhost:3000

# Edge worker
cd edge && npm ci && npm test
```

### Configuration (.env)

All settings live in one git-ignored `.env` at the repository root. Docker Compose, the API and workers ([DotNetEnv](https://github.com/tonerdo/dotnet-env)) and the web app (`web/next.config.ts`) all read it. `.env.example` lists every key.

```bash
cp .env.example .env   # then fill in the secrets
```

### Whole stack in Docker

Runs PostgreSQL, RustFS (S3-compatible stand-in for R2), the API, the workers and the web app. Only Docker is needed.

```bash
docker compose up -d --build --wait
```

| Service | URL |
|---|---|
| Web | http://localhost:3000 |
| API liveness / readiness (DB) | http://localhost:5080/health/live · http://localhost:5080/health/ready |
| API docs (Scalar, not in production) | http://localhost:5080/scalar |
| Storage console | http://localhost:9001/rustfs/console/ |
| PostgreSQL | `localhost:5432` |

Every port is bound to `127.0.0.1` only. The API answers browsers only from the origins in `Cors__AllowedOrigins`.
Stop with `docker compose down` (add `--volumes` to wipe the data).

## Project structure

```
SS-SPORT Drive/
├── src/
│   ├── SsSport.Drive.Domain/           # Entities, aggregates, value objects, domain events
│   ├── SsSport.Drive.Application/      # MediatR commands & queries, DTOs, specifications
│   ├── SsSport.Drive.Infrastructure/   # EF Core, Unit of Work, repositories, external services
│   ├── SsSport.Drive.Api/              # HTTP API host (OpenAPI + Scalar)
│   └── SsSport.Drive.Workers/          # Background job host (Hangfire)
├── tests/                              # Unit, architecture & integration tests
├── web/                                # Next.js front-end
├── edge/                               # Cloudflare Worker
├── deploy/                             # Staging/production deployment & operations
├── docker-compose.yml                  # Local stack (reads the root .env)
├── .env.example                        # Every setting; copy to .env
└── .github/workflows/                  # CI pipeline
```

## Engineering rules

- 🌿 **One branch per feature:** `feature/<area>-<short-name>`, merged into `main` by pull request with green CI.
- 🧪 **Tests first:** every handler, endpoint and domain rule starts as a failing test. Nothing is committed red.
- 🧱 **Architecture:** MediatR for every command and query, Specification pattern for query criteria, DTOs at every API boundary.
- 📝 **Docs in code:** XML docs on every .NET member (the build fails without them), TSDoc on exported TypeScript.
- 📚 **API docs:** every endpoint is fully described in OpenAPI and rendered with Scalar.
- 🔒 **No secrets in git:** configuration comes from environment variables and secrets only.
- 🌐 **English** in code, comments, logs and commits.
- 📦 **NuGet versions** live only in `Directory.Packages.props`.

## Roadmap

| # | Epic | Status |
|---|---|---|
| 0 | Setup & accounts | 🚧 In progress |
| 1 | Core domain & persistence | 🚧 In progress |
| 2 | Telegram bot & Mini App | ⏳ Planned |
| 3 | Ingest, transcode & sync | ⏳ Planned |
| 4 | Viewer site & media protection | ⏳ Planned |
| 5 | Personal links, OTP & tracking | ⏳ Planned |
| 6 | Public search & access requests | ⏳ Planned |
| 7 | Admin console | ⏳ Planned |
| 8 | Archive migration | ⏳ Planned |
| 9 | Deploy & operations | ⏳ Planned |
| 10 | Performance (k6) | ⏳ Planned |

Work is tracked in Jira (project **SSD**).

---

<div align="center">

**Proprietary software.** Copyright © 2026 SS-SPORT. All rights reserved. See [LICENSE](LICENSE).

[info@ss-sport.co](mailto:info@ss-sport.co)

</div>
