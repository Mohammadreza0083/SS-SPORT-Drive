# deploy

Everything SS-SPORT Drive needs to run, as Docker:

- `docker-compose` for local development: PostgreSQL, MinIO (R2 stand-in), API, workers (backlog S-10).
- Production / staging compose files, Grafana + Loki, and the runbook (backlog O-1, O-3, O-5).

Configuration always comes from environment variables and secrets, never from committed files.
