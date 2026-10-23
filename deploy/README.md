# deploy

Staging/production deployment and operations for SS-SPORT Drive, as Docker. Configuration always comes from environment variables and secrets, never from committed files.

## Local stack

The local development stack lives at the repository root: [`docker-compose.yml`](../docker-compose.yml), configured by the root `.env` (template: [`.env.example`](../.env.example)).

```bash
cp .env.example .env      # once; .env is git-ignored
docker compose up -d --build --wait
```

| Service | What it is | Host port (127.0.0.1 only) |
|---|---|---|
| `postgres` | PostgreSQL 17 (app data + Hangfire storage) | 5432 |
| `storage` | RustFS, S3-compatible stand-in for Cloudflare R2 (console at `/rustfs/console/`) | 9000 (S3), 9001 (console) |
| `storage-init` | One-shot job that creates the bucket, then exits | – |
| `api` | ASP.NET Core API: `/health/live`, `/health/ready` (checks PostgreSQL) | 5080 |
| `workers` | Hangfire worker host | – |
| `web` | Next.js app (production build) | 3000 |

- The apps get every `.env` key (Telegram, email, R2, CORS) through `env_file`. The compose file overrides only the database host and storage URL, so they point at the Docker services instead of `localhost`.
- Pointing `Storage__*` at Cloudflare R2 instead of RustFS needs no code change.
- **Why RustFS, not MinIO:** MinIO no longer publishes Docker images. RustFS (Apache-2.0) speaks the same S3 API and has a similar console.

CI runs the same stack on every PR (`compose` job) and probes the API (live + ready) and the web app.

## Coming next

Production / staging compose files, Grafana + Loki, and the runbook (backlog O-1, O-3, O-5).
