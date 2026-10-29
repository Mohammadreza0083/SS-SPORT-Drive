# k6 benchmarks

Load tests for SS-SPORT Drive's user-facing endpoints. Each script runs a `smoke` profile (short and light, safe on any environment) and a `load` profile (several minutes up to a peak rate). Results go to `k6-results/`, which git and Docker ignore.

The image is pinned to `grafana/k6:2.2.0`, so you don't need k6 installed.

## `telegram-webhook.js`: `POST /telegram/webhook` (T-1)

Three traffic shapes run at the same time:

| Scenario | What it sends | Expected | Threshold (p95) |
|---|---|---|---|
| `new_updates` | Fresh updates, alternating text messages and button presses, each with a new `update_id` | 200, one `telegram_updates` row each | < 250 ms (p99 < 500 ms) |
| `redeliveries` | Updates Telegram sends again (same `update_id`) | 200, skipped as duplicates | < 200 ms |
| `wrong_secret` | Calls with a wrong secret token | 401, no database work | < 100 ms |

Across the whole run, fewer than 1 % of requests may fail and more than 99 % of checks must pass. The 401s count as expected.

| Profile | Shape |
|---|---|
| `smoke` (default) | 30 s: 5 new + 2 repeated + 1 wrong-secret update per second |
| `load` | Ramps new updates to `PEAK_RATE`/s (default 50) over 1 min, holds 2 min, ramps down 30 s; repeats at peak/5 and wrong secrets at peak/10 |

A small bot rarely gets more than a few updates per second, so the default peak of 50/s leaves a wide margin.

### Run it locally (Docker)

1. Give the API a webhook secret in `.env` (any 1–256 characters of `A-Z a-z 0-9 _ -`), then start the stack:

   ```bash
   docker compose up -d --build --wait
   ```

2. Run the smoke profile against it. Inside the k6 container, the host is `host.docker.internal`:

   ```bash
   docker run --rm -v "$PWD:/work" -w /work grafana/k6:2.2.0 run -e BASE_URL=http://host.docker.internal:5080 -e WEBHOOK_SECRET=<the secret> -e PROFILE=smoke --summary-export k6-results/telegram-webhook-smoke.json perf/k6/telegram-webhook.js
   ```

   For the full run, use `-e PROFILE=load`, and optionally `-e PEAK_RATE=100`.

With k6 installed, drop the `docker run …` prefix and use `BASE_URL=http://localhost:5080`.

### Notes

- **It writes data.** Every new update adds a row to `telegram_updates`, so a `load` run adds about 10 000. Run it on local or staging only, never on production.
- **No real Telegram traffic is involved.** The script talks to the API directly, so no VPN is needed.
- **Each run gets its own `update_id` window** of 60 000 ids, chosen by the start minute, so ids stay 32-bit like Telegram's. Two runs started in the same minute share a window, so wait a minute between runs.
- **CI runs the smoke profile** against staging after each deploy once K-1 is in place.

## `miniapp.js`: the Mini App API (T-3, T-4a)

Every request signs in with Telegram-signed `initData` (`Authorization: tma <initData>`). The script builds it exactly as Telegram does, with the bot token, so the API runs its full HMAC check and user lookup on every call. Six traffic shapes run at the same time:

| Scenario | Endpoint | Expected | Threshold (p95) |
|---|---|---|---|
| `profile` | `GET /miniapp/me` | 200 | < 150 ms (p99 < 300 ms) |
| `countries` | `GET /miniapp/countries` | 200, 245 countries | < 150 ms |
| `volleybox` | `GET /miniapp/volleybox?url=…` (a new id each time) | 200 | < 150 ms |
| `create_player` (opt-in, `CREATE_PLAYERS=1`) | `POST /miniapp/players` (a new name, Volleybox id and Drive folder each time) | 201 | < 400 ms (p99 < 800 ms) |
| `forged` | `GET /miniapp/me` signed for another bot | 401, no database work | < 100 ms |
| `not_member` | `GET /miniapp/me` as a Telegram user outside the team | 403 | < 150 ms |
| `photo_upload` (opt-in, `PHOTO_PLAYERS`) | `PUT /miniapp/players/{id}/photo` with a photo-like 1200×1600 JPEG of 625 KB (`fixtures/`) | 200 | < 2000 ms |
| `photo_read` (opt-in, `PHOTO_PLAYERS`) | `GET /miniapp/players/{id}/photo` | 200, WebP | < 150 ms |
| `drive_check` (opt-in, `DRIVE_FOLDER_ID`) | `GET /miniapp/drive-folder` against **real Google Drive**, fixed at 1/s (smoke) or 2/s (load) for the API quota | 200 | < 3000 ms |

Across the whole run, fewer than 1 % of requests may fail and more than 99 % of checks must pass. The 401s and 403s count as expected.

| Profile | Shape |
|---|---|
| `smoke` (default) | 30 s: 10 profile, 5 Volleybox, 3 countries, 2 create, 2 forged and 1 stranger request per second |
| `load` | Ramps to `PEAK_RATE` profile reads/s (default 50) over 1 min, holds 2 min, ramps down 30 s. The other scenarios run at their share of the peak: Volleybox 40 %, countries 20 %, forged 20 %, create 10 %, stranger 10 % |

### Run it locally (Docker)

1. **Use a throwaway database and a test bot token.** The script creates players and folders, so don't run it against your dev data. It needs the API's bot token to sign, so give the API a test token, not the real one:

   ```bash
   docker run -d --name ssd-bench-pg -e POSTGRES_USER=bench -e POSTGRES_PASSWORD=bench -e POSTGRES_DB=ss_sport_drive_bench -p 127.0.0.1:55432:5432 postgres:17-alpine
   ```

2. **Start the API in Release mode against it.** Real environment variables win over `.env`:

   ```bash
   dotnet build src/SsSport.Drive.Api -c Release
   export ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS=http://127.0.0.1:5081 Telegram__Mode=Webhook Telegram__BotToken=424242:k6-bench-token ConnectionStrings__DefaultConnection="Host=127.0.0.1;Port=55432;Database=ss_sport_drive_bench;Username=bench;Password=bench"
   dotnet src/SsSport.Drive.Api/bin/Release/net10.0/SsSport.Drive.Api.dll migrate
   dotnet src/SsSport.Drive.Api/bin/Release/net10.0/SsSport.Drive.Api.dll
   ```

3. **Add an active analyst for the script to sign in as:**

   ```bash
   docker exec ssd-bench-pg psql -U bench -d ss_sport_drive_bench -c "INSERT INTO users (id, telegram_id, username, display_name, role, status, requested_at) VALUES (gen_random_uuid(), 777000001, 'k6_analyst', 'K6 Analyst', 'Analyst', 'Active', now());"
   ```

4. **Run it:**

   ```bash
   docker run --rm -v "$PWD:/work" -w /work grafana/k6:2.2.0 run -e BASE_URL=http://host.docker.internal:5081 -e BOT_TOKEN=424242:k6-bench-token -e ANALYST_TELEGRAM_ID=777000001 -e PROFILE=smoke --summary-export k6-results/miniapp-smoke.json perf/k6/miniapp.js
   ```

   For the full run, use `-e PROFILE=load`, and optionally `-e PEAK_RATE=200`. Remove the database afterwards with `docker rm -f ssd-bench-pg`.

### Results (2026-10-29, local Windows machine, API in Release, PostgreSQL 17 in Docker)

| Run | Requests | Failed | p95 profile | p95 Volleybox | p95 create | p99 create | Rate |
|---|---|---|---|---|---|---|---|
| `smoke` | 695 | 0 | 13.6 ms | 12.8 ms | 38.1 ms | 185 ms (first requests, cold start) | 22/s |
| `load`, peak 50 | 16 681 | 0 | 12.4 ms | 14.0 ms | 32.1 ms | 39.5 ms | 79/s avg |
| `load`, peak 200 | 66 179 | 0 | 10.1 ms | 10.4 ms | 27.3 ms | 49.6 ms | 315/s avg |

Every threshold passed with a wide margin, even at 4× the default peak. A handful of analysts will never come close to these rates. The 4 246 players created in the peak-200 run all got distinct slugs and folders.

### Photo and Drive scenarios (T-4b, T-4c)

These need object storage and, for `drive_check`, the real Shared Drive:

1. **Start a throwaway RustFS next to the database and create a bucket:**

   ```bash
   docker run -d --name ssd-bench-s3 -e RUSTFS_ACCESS_KEY=benchkey -e RUSTFS_SECRET_KEY=benchsecret123 -p 127.0.0.1:59000:9000 rustfs/rustfs:1.0.3
   docker run --rm -e AWS_ACCESS_KEY_ID=benchkey -e AWS_SECRET_ACCESS_KEY=benchsecret123 -e AWS_DEFAULT_REGION=us-east-1 amazon/aws-cli:2.38.5 --endpoint-url http://host.docker.internal:59000 s3api create-bucket --bucket ssd-bench
   ```

2. **Start the API** with `Storage__ServiceUrl=http://127.0.0.1:59000 Storage__AccessKey=benchkey Storage__SecretKey=benchsecret123 Storage__Bucket=ssd-bench`. For `drive_check`, also set the real `GoogleDrive__ServiceAccountKeyPath` (absolute) and `GoogleDrive__SharedDriveId`, plus `GoogleDrive__Proxy` where Google needs a VPN.

3. **Seed players for the analyst.** The ids follow the pattern the script expects:

   ```bash
   docker exec ssd-bench-pg psql -U bench -d ss_sport_drive_bench -c "INSERT INTO players (id, category, created_at, status, analyst_id) SELECT ('0199f000-0000-7000-8000-' || lpad(n::text, 12, '0'))::uuid, 'Men', now(), 'Waiting', (SELECT id FROM users WHERE telegram_id = 777000001) FROM generate_series(1, 200) AS n;"
   ```

4. **Run with `-e PHOTO_PLAYERS=200`**, and `-e DRIVE_FOLDER_ID=<a real, unlinked player folder id>` for the Drive check.

**Results (2026-10-29, same machine, Drive through the owner's VPN from Iran):**

| Run | Requests | Failed | p95 upload | p99 upload | p95 read | p95 Drive check | p99 Drive check |
|---|---|---|---|---|---|---|---|
| `smoke` | 807 | 0 | 543 ms | 553 ms | 60 ms | 2.81 s | 4.05 s |
| `load`, peak 50 | 19 291 | 0 | 484 ms | 549 ms | 59 ms | 2.28 s | 2.57 s |

All thresholds passed, and every one of the 200 players got its photo.
- **Photo upload:** about 250–500 ms, almost all of it Magick decoding the JPEG and encoding the WebP.
- **Drive check:** about 2 s, most of it the round trips to Google through the VPN. On the VPS (no VPN, close to Google) expect a fraction of that. Re-measure there with K-1.

### Notes

- **`create_player` is opt-in since T-4c.** Creating a player now reads its folder from Google Drive with the service account, and the script's made-up folder ids do not exist there, so against real Drive they are refused with 422. The results above were measured before T-4c, without that Drive call. Benchmark creation on staging with real folders (K-1), or with a test double for the Drive reader.
- **It writes data.** Each `create_player` iteration adds a player and a Drive folder; a peak-200 `load` run adds about 4 000 of each. Run it on a throwaway database or on staging only, never on production.
- **Each run gets its own Volleybox id window** of 100 000 ids, chosen by the start minute. Two runs started in the same minute share a window, and `create_player` would then hit `player.volleybox_taken`. Wait a minute between runs.
- **The bot token never leaves the machine:** k6 only uses it to sign. It is not logged, and the API logs only rejection reasons.
