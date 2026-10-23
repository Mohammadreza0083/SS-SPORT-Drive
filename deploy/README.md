# deploy

Everything SS-SPORT Drive needs to run, as Docker. Configuration always comes from environment variables and secrets, never from committed files.

## Local stack (backlog S-10)

```bash
cp deploy/.env.example deploy/.env      # once; deploy/.env is git-ignored
docker compose -f deploy/docker-compose.yml up -d --build --wait
```

| Service | What it is | Host port |
|---|---|---|
| `postgres` | PostgreSQL 17 (app data + Hangfire storage) | 5432 |
| `storage` | RustFS, S3-compatible stand-in for Cloudflare R2 (console at `/rustfs/console/`) | 9000 (S3), 9001 (console) |
| `storage-init` | One-shot job that creates the bucket, then exits | – |
| `api` | ASP.NET Core API, health at `/health/live` | 8080 |
| `workers` | Hangfire worker host | – |
| `web` | Next.js app (production build) | 3000 |

The apps receive their settings as `ConnectionStrings__Database` and `Storage__ServiceUrl` / `AccessKey` / `SecretKey` / `Bucket`. Pointing `Storage__*` at Cloudflare R2 instead of RustFS needs no code change.

**Why RustFS, not MinIO:** MinIO no longer publishes Docker images. RustFS (Apache-2.0) speaks the same S3 API and has a similar console.

CI runs the same stack on every PR (`compose` job) and probes the API and the web app.

## Coming next

Production / staging compose files, Grafana + Loki, and the runbook (backlog O-1, O-3, O-5).
