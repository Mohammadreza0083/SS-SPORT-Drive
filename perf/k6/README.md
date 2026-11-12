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
- **Telegram is called for every new text update.** Since T-2 and T-10a, the bot answers a `/start` inline, because the user is waiting (T-6c). So each `new_updates` request includes one real Bot API round trip to `api.telegram.org`. With the benchmark's fake token that call fails with 401 after the round trip (logged as an Error, answered 200). The `new_updates` latency is therefore mostly Telegram's network latency from the machine running the API. Redeliveries and wrong secrets never call Telegram.
- **Each run gets its own `update_id` window** of 60 000 ids, chosen by the start minute, so ids stay 32-bit like Telegram's. Two runs started in the same minute share a window, so wait a minute between runs.
- **CI runs the smoke profile** against staging after each deploy once K-1 is in place.

## `miniapp.js`: the Mini App API (T-3, T-4a, T-5, T-7a, T-7b)

Every request signs in with Telegram-signed `initData` (`Authorization: tma <initData>`). The script builds it exactly as Telegram does, with the bot token, so the API runs its full HMAC check and user lookup on every call. Six traffic shapes run at the same time:

| Scenario | Endpoint | Expected | Threshold (p95) |
|---|---|---|---|
| `profile` | `GET /miniapp/me` | 200 | < 150 ms (p99 < 300 ms) |
| `countries` | `GET /miniapp/countries` | 200, 245 countries | < 150 ms |
| `volleybox` | `GET /miniapp/volleybox?url=…` (a new id each time) | 200 | < 150 ms |
| `create_player` (opt-in, `CREATE_PLAYERS=1`) | `POST /miniapp/players` (a new name, Volleybox id and Drive folder each time) | 201 | < 400 ms (p99 < 800 ms) |
| `forged` | `GET /miniapp/me` signed for another bot | 401, no database work | < 100 ms |
| `not_member` | `GET /miniapp/me` as a Telegram user outside the team | 403 | < 150 ms |
| `player_card` (opt-in, `SEEDED_PLAYERS`) | `GET /miniapp/players/{id}`, the update-mode card (T-5) | 200, editable by the owner | < 150 ms (p99 < 300 ms) |
| `player_edit` (opt-in, `SEEDED_PLAYERS`) | `PATCH /miniapp/players/{id}`, nationality and positions (T-5) | 200 | < 300 ms (p99 < 600 ms) |
| `photo_upload` (opt-in, `SEEDED_PLAYERS`) | `PUT /miniapp/players/{id}/photo` with a photo-like 1200×1600 JPEG of 625 KB (`fixtures/`) | 200 | < 2000 ms |
| `photo_read` (opt-in, `SEEDED_PLAYERS`) | `GET /miniapp/players/{id}/photo` | 200, WebP | < 150 ms |
| `my_players` (opt-in, `SEEDED_PLAYERS`) | `GET /miniapp/players?page=…`, paging through My players (T-7a) | 200, a non-empty page | < 150 ms (p99 < 300 ms) |
| `my_players_filter` (opt-in, `SEEDED_PLAYERS`) | `GET /miniapp/players?filter=…` with the four chips in turn, half of them with `search=bench` (T-7a) | 200 | < 200 ms (p99 < 400 ms) |
| `player_files` (opt-in, `SEEDED_PLAYERS`) | `GET /miniapp/players/{id}/files`, the player detail's files in page order (T-7a) | 200, 5 files | < 150 ms (p99 < 300 ms) |
| `file_edit` (opt-in, `SEEDED_PLAYERS`) | `PATCH /miniapp/players/{id}/files/{fileId}`, a new title and tags from the edit-video sheet (T-7b) | 200 | < 300 ms (p99 < 600 ms) |
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

2. **Start the API in Release mode against it.** Real environment variables win over `.env`. Set **both** `ASPNETCORE_ENVIRONMENT` and `DOTNET_ENVIRONMENT`: the API loads the repository's `.env` (it walks up from the current folder), and a `DOTNET_ENVIRONMENT=Development` there would otherwise put the API in Development mode. **Run it from its output folder:** the API reads `appsettings.json` from the current folder, so started from the repository root it runs with no settings file at all. Every log level then falls back to Information, and EF Core logs every SQL statement, which slows the whole API down under load. From the output folder, D-14's levels apply: SQL commands are not logged, and slow ones are Warnings.

   ```bash
   dotnet build src/SsSport.Drive.Api -c Release
   export DOTNET_ENVIRONMENT=Production ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS=http://127.0.0.1:5081 Telegram__Mode=Webhook Telegram__BotToken=424242:k6-bench-token ConnectionStrings__DefaultConnection="Host=127.0.0.1;Port=55432;Database=ss_sport_drive_bench;Username=bench;Password=bench"
   cd src/SsSport.Drive.Api/bin/Release/net10.0
   dotnet SsSport.Drive.Api.dll migrate
   dotnet SsSport.Drive.Api.dll
   ```

   Before you start k6, check two things: the log says `Hosting environment: Production`, and it does not print `Executed DbCommand` lines. Also check `docker ps`: other test runs on the machine skew the results.

3. **Add an active analyst for the script to sign in as:**

   ```bash
   docker exec ssd-bench-pg psql -U bench -d ss_sport_drive_bench -c "INSERT INTO users (id, telegram_id, username, display_name, role, status, requested_at) VALUES (gen_random_uuid(), 777000001, 'k6_analyst', 'K6 Analyst', 'Analyst', 'Active', now());"
   ```

4. **Run it:**

   ```bash
   docker run --rm -v "$PWD:/work" -w /work grafana/k6:2.2.0 run -e BASE_URL=http://host.docker.internal:5081 -e BOT_TOKEN=424242:k6-bench-token -e ANALYST_TELEGRAM_ID=777000001 -e PROFILE=smoke --summary-export k6-results/miniapp-smoke.json perf/k6/miniapp.js
   ```

   For the full run, use `-e PROFILE=load`, and optionally `-e PEAK_RATE=200`. Remove the database afterwards with `docker rm -f ssd-bench-pg`.

### Running against the security baseline (O-9, since 2026-11-01)

Outside Development the API redirects plain HTTP to HTTPS (308), and it allows 300 requests per client per minute (1 800 for the webhook). Both scripts therefore send `X-Forwarded-Proto: https`, as the reverse proxy does. The API trusts it from loopback, which includes Docker Desktop's `host.docker.internal`. For a load run, also start the API with higher budgets, for the benchmark only:

```bash
export RateLimiting__PermitsPerMinute=1000000 RateLimiting__WebhookPermitsPerMinute=1000000
```

### Results (2026-11-12, T-8 branch: security baseline, D-15 pool cap, T-10a bot chat and T-8 notices all in)

Same machine and recipe as below: API in Release (Production), PostgreSQL 17 in Docker, rate limits raised as above. Another worktree's compose stack (API, workers, web, PostgreSQL, RustFS) was running idle on the same machine.

| Run | Requests | Failed | p95 profile | p99 profile | p95 Volleybox | p95 countries | p95 stranger (403) | Rate |
|---|---|---|---|---|---|---|---|---|
| `miniapp.js` `smoke` | 634 | 0 | 14.7 ms | 34.3 ms | 17.3 ms | 15.2 ms | 22.2 ms | 20/s |
| `miniapp.js` `load`, peak 200 | 62 837 | 0 | 52.6 ms | 120 ms | 52.5 ms | 43.3 ms | 40.1 ms | 299/s avg |

| Run | Requests | Failed | p95 new updates | p99 new updates | p95 redeliveries | p95 wrong secret | Rate |
|---|---|---|---|---|---|---|---|
| `telegram-webhook.js` `load`, peak 50 | 11 446 | 0 | **364 ms ✗** (< 250) | **728 ms ✗** (< 500) | 64.4 ms | 38.5 ms | 53/s avg |

- **Mini App:** every threshold passed, with 0 failed requests and 100 % of checks. There were no `too many clients` errors at 4× the default peak, so the D-15 pool cap holds. p95 is about 5× the 2026-10-29 run at the same peak (52.6 vs 10.1 ms). Since then the security baseline (rate limiter, headers, forwarded headers) and the T-10a/T-8 code have landed, and this run shared the machine with another stack. A rerun on a quiet machine should tell which of these it is.
- **Webhook:** the redelivery and wrong-secret thresholds pass. The `new_updates` thresholds fail because each such update waits for a real call to Telegram (see the notes on `telegram-webhook.js`): the fastest one took 99 ms. The handler itself is not the slow part. Measuring our own code needs a Bot API stand-in (follow-up).
- **T-8** adds no endpoint. Adding a folder only queues its notice (T-6c), and the notice job and the every-minute `bot-notices-due` round run outside the request.

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

4. **Run with `-e SEEDED_PLAYERS=200`** (the older `PHOTO_PLAYERS` still works), and `-e DRIVE_FOLDER_ID=<a real, unlinked player folder id>` for the Drive check.

**Results (2026-10-29, same machine, Drive through the owner's VPN from Iran):**

| Run | Requests | Failed | p95 upload | p99 upload | p95 read | p95 Drive check | p99 Drive check |
|---|---|---|---|---|---|---|---|
| `smoke` | 807 | 0 | 543 ms | 553 ms | 60 ms | 2.81 s | 4.05 s |
| `load`, peak 50 | 19 291 | 0 | 484 ms | 549 ms | 59 ms | 2.28 s | 2.57 s |

All thresholds passed, and every one of the 200 players got its photo.
- **Photo upload:** about 250–500 ms, almost all of it Magick decoding the JPEG and encoding the WebP.
- **Drive check:** about 2 s, most of it the round trips to Google through the VPN. On the VPS (no VPN, close to Google) expect a fraction of that. Re-measure there with K-1.

### Update-mode scenarios (T-5)

`player_card` and `player_edit` use the same seeded players. Give each one a folder so the card has something to count:

```bash
docker exec ssd-bench-pg psql -U bench -d ss_sport_drive_bench -c "INSERT INTO drive_folders (id, player_id, drive_folder_id, name, linked_at, access) SELECT gen_random_uuid(), ('0199f000-0000-7000-8000-' || lpad(n::text, 12, '0'))::uuid, 'benchFolder_' || lpad(n::text, 6, '0'), 'SS-SPORT Bench ' || n, now(), 'Ok' FROM generate_series(1, 200) AS n;"
```

`POST /miniapp/players/{id}/folders` is not benchmarked here. Every call reads a real, unlinked Drive folder and links it for good, so it is measured on staging (K-1), like `create_player`.

**Results (2026-10-29, same machine, without `drive_check`):**

| Run | Requests | Failed | p95 card | p99 card | p95 edit | p99 edit | p95 upload |
|---|---|---|---|---|---|---|---|
| `smoke` | 990 | 1 (0.1 %) | 35 ms | 81 ms | 38 ms | 90 ms | 541 ms |
| `load`, peak 50 | 23 058 | 1 (< 0.01 %) | 31 ms | 55 ms | 32 ms | 57 ms | 318 ms |

Every threshold passed. **The benchmark found a bug:** the one failed request in each run was a photo upload that raced a details edit of the same player. The optimistic concurrency check (`xmin`) correctly refused the second write, but the API answered **500 instead of 409**. Fixing that, and no longer logging expected 401/403 responses as errors, is D-13 (SSD-113).

### My players scenarios (T-7a, T-7b)

`my_players`, `my_players_filter`, `player_files` and `file_edit` use the same seeded players and folders as the update-mode scenarios. They also need names, so the search has something to match, and 5 files per player with the ids the script expects (`0199f100-0000-7000-8000-{player × 10 + file}`). Every 7th player gets a failed file and every 10th a folder that lost access, so each filter chip has rows:

```bash
docker exec -i ssd-bench-pg psql -U bench -d ss_sport_drive_bench <<'SQL'
UPDATE players SET full_name = 'Bench ' || initcap(chr(97 + (n % 26)) || chr(97 + ((n / 26) % 26)) || 'player')
FROM (SELECT generate_series(1, 200) AS n) AS s
WHERE players.id = ('0199f000-0000-7000-8000-' || lpad(s.n::text, 12, '0'))::uuid;
UPDATE drive_folders SET access = 'Lost' WHERE drive_folder_id IN (SELECT 'benchFolder_' || lpad(n::text, 6, '0') FROM generate_series(10, 200, 10) AS n);
INSERT INTO assets (id, player_id, created_at, source, state, failure_reason, tags, title, title_is_custom, version, visibility,
                    sort_order, kind, md5, original_name, size_bytes)
SELECT ('0199f100-0000-7000-8000-' || lpad((p * 10 + f)::text, 12, '0'))::uuid,
       ('0199f000-0000-7000-8000-' || lpad(p::text, 12, '0'))::uuid, now(), 'Drive',
       CASE WHEN f = 5 AND p % 7 = 0 THEN 'Failed' WHEN f >= 4 THEN 'Queued' ELSE 'Ready' END,
       CASE WHEN f = 5 AND p % 7 = 0 THEN 'ffmpeg exited with code 1' END,
       1, 'Bench clip ' || f, false, 1, 'Visible', f - 1, 'Video', md5(p || '-' || f), 'clip-' || f || '.mp4', 1048576
FROM generate_series(1, 200) AS p, generate_series(1, 5) AS f;
SQL
```

Keep the seeded players Waiting: the update-mode scenarios edit their details, which only an admin may do once a player is published.

**Results (2026-10-31, same machine, API in Release, without `drive_check`):**

| Run | Requests | Failed | p95 / p99 list | p95 / p99 filter + search | p95 / p99 files | p95 / p99 file edit |
|---|---|---|---|---|---|---|
| `smoke` (Development mode, SQL logged; before the recipe fix) | 1 421 | 0 | 38 / 57 ms | 47 / 65 ms | 33 / 54 ms | 33 / 54 ms |
| `load`, peak 50 (Production mode, SQL logging at Warning) | 32 226 | 2 (photo uploads) | 66 / 133 ms | 67 / 142 ms | 64 / 142 ms | 59 / 134 ms |

Every T-7 threshold passed with a wide margin. The list runs one SQL query with correlated counts of each player's files and folders, plus the total, and stays near the single-player card's time.

**One older threshold failed here: `player_edit` (T-5); fixed by T-6c, see below.** Its p95 was 845 ms (smoke, Development mode) and 929 ms (load, Production mode), against 300 ms. This is not T-7. Since T-6a, every saved player change sends or edits the analyst's progress message in Telegram **inside the request**. With the bench's fake token, each call is a round trip to `api.telegram.org` that ends in 401, and from this machine that round trip takes about 1 s. Photo uploads pay the same cost (p95 1.7 s, against 0.5 s on 10-29). On the VPS, Telegram is close and the token is real, but the request still waits for Telegram. Sending progress messages after the response (an outbox or a background job) would remove it.

**Two run-recipe traps found on the way, now fixed above:**
- **Development mode.** The API loaded the repository's `.env` and ran in **Development** mode. In that mode, with every SQL statement logged (90 MB of console output in one load run), even a 401 that never touches the database took 686 ms at p95. In Production mode with SQL logging at Warning, the same load gives it a p95 of 22 ms.
- **Volleybox ids.** The script's Volleybox ids went up to 10 digits in about half of all minutes, and the domain allows 9, so every `volleybox` check was refused with 422. The id window now stays within 9 digits.

### Bot notices out of the request (T-6c)

Since T-6c, bot notices are queued as Hangfire jobs and sent after the response, so `player_edit` and `photo_upload` no longer wait for Telegram. Same machine, same recipe and seed as K-9, load at peak 50 (Production mode, SQL logging at Warning, the API's Hangfire server on):

| Scenario | p95 before (K-9) | p95 after | p99 after |
|---|---|---|---|
| `player_edit` | 929 ms ✗ | **71 ms** | 113 ms |
| `photo_upload` | 1.71 s | **612 ms** | 751 ms |
| `my_players` | 66 ms | 44 ms | 67 ms |
| `file_edit` | 59 ms | 43 ms | 71 ms |
| `profile` | 38 ms | 26 ms | 46 ms |

32 249 requests at 146/s, 2 failed (photo uploads). **Every threshold passed.**

**The queue falls behind under load.** One worker sends the notices in order. With the bench's fake token, each Telegram call took about 1 s from this machine, so the worker managed about 1 notice/s while the load raised about 10/s. When the run ended, 1 061 notice jobs had succeeded, none had failed, and 1 311 were still queued. They go out over the next minutes. Real traffic is a few analysts, and on the VPS a Telegram call takes a fraction of that time, so this does not show up there. Options if it ever does: more workers with ordering per player, or merging the pending refreshes of one player into one.

**Leaked test containers skew results.** An earlier crashed test run had left 234 PostgreSQL containers running, which made the whole machine slow: `profile` p95 was 421 ms. Check `docker ps` before a run.

### SQL logging (D-14)

Since D-14, EF Core logs at Warning: no SQL command is logged unless it fails or takes at least 500 ms. Two load runs at peak 50 measure the difference. Both ran back to back on 2026-11-01, Production mode, from the API's output folder; the only change was `Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command=Information` for the second.

| Scenario | p95, SQL not logged (D-14) | p95, every SQL command logged | Change |
|---|---|---|---|
| `profile` | 135 ms | 274 ms | about 2× |
| `my_players` | 181 ms | 372 ms | about 2× |
| `player_card` | 228 ms | 441 ms | about 1.9× |
| `file_edit` | 219 ms | 346 ms | about 1.6× |
| `player_edit` | 437 ms | 534 ms | about 1.2× |
| `photo_upload` | 3.56 s | 4.49 s | about 1.3× |
| API log | 8.9 MB, 0 SQL lines, 0 slow commands | 55 MB, 82 916 SQL lines | |

32 201 and 32 088 requests, 9 and 13 failed. Under load, logging every SQL statement roughly doubled response times.

**Absolute numbers are higher than in the T-6c run** because another session's test suite was running on the machine the whole time. That is also why some thresholds failed in both runs, and why only the side-by-side comparison counts here. Re-measure on staging (K-1).

**Found on the way: database connections run out under load.** In one run, PostgreSQL refused new connections ("too many clients already"), and a few requests failed. Npgsql's pool allows 100 connections by default, which is also PostgreSQL's default `max_connections`, a few of them reserved. The API, Hangfire and a `psql` session share that limit. Fixed in D-15: the API's pool is capped at 40 and the workers' at 20, and a busy database is answered with 503 `database.busy` instead of 500. Re-run a `load` scenario to confirm no "too many clients" errors.

### Notes

- **`create_player` is opt-in since T-4c.** Creating a player now reads its folder from Google Drive with the service account, and the script's made-up folder ids do not exist there, so against real Drive they are refused with 422. The results above were measured before T-4c, without that Drive call. Benchmark creation on staging with real folders (K-1), or with a test double for the Drive reader.
- **It writes data.** Each `create_player` iteration adds a player and a Drive folder; a peak-200 `load` run adds about 4 000 of each. Run it on a throwaway database or on staging only, never on production.
- **Each run gets its own Volleybox id window** of 100 000 ids between 100 000 000 and 999 999 999 (Volleybox ids have at most 9 digits), chosen by the start minute. Two runs started in the same minute share a window, and `create_player` would then hit `player.volleybox_taken`. Wait a minute between runs.
- **The bot token never leaves the machine:** k6 only uses it to sign. It is not logged, and the API logs only rejection reasons.

## Personal link gate with forwarding (L-3) and clean-up (L-2b)

L-3 adds one indexed lookup (`link_forwards` by link and email) to `POST /links/{code}/email-code` for forward emails, and to `/verify` and `/session` for forwards. L-2b adds no request work: it is a daily job. The gate scenarios of `viewer-links.js` cover these endpoints. That script arrives with V-2b (PR #57), so this run used its copy from that branch, with no `SESSION_TOKEN`: only the gate scenarios ran. `email_code` sends each request from a fresh forward address, so it runs the new lookup every time.

### Results (2026-11-12, local Windows machine, API in Release as Staging, PostgreSQL 17 in Docker, pool 40)

Seed: one published player on one Personal link, made in SQL. Codes went to a pickup directory (`Email__PickupDirectory`; Production refuses it, hence Staging). Rate limits were raised as above, and `Security__TrustedProxies=0.0.0.0/0,::/0` was set. Two other local stacks (`ssd-live`, `ssd-hardening`) were running idle on the same machine.

| Run (`SEND_CODES=1`) | Requests | Rate | Failed | p95 gate card | p95 unknown link | p95 wrong code | p95 stale device | p95 email code |
|---|---|---|---|---|---|---|---|---|
| `smoke` | 365 | 12/s | 0 | 24.4 ms | 15.1 ms | 23.3 ms | 23.6 ms | 82.3 ms |
| `load`, peak 20 | 2 296 | 11/s | 0 | 12.8 ms | 9.5 ms | 12.1 ms | 13.0 ms | 46.2 ms |
| `load`, peak 50 | 5 593 | 27/s | 0 | 21.9 ms | 14.5 ms | 19.0 ms | 19.3 ms | 69.1 ms |

- Every threshold passed, and all checks passed (0 failed of 8 251). All 761 email jobs succeeded.
- The rates are lower than V-2b's runs at the same `PEAK_RATE`, because the viewer scenarios (most of the traffic) did not run. The numbers measure the gate alone.
- A manual pass on the same build confirmed the forward flow: code request 202; verify 200 with `awaitingApproval: true`, a device token and no session; `/session` 403 `share_link.forward_pending`; one `Pending` row in `link_forwards`.
- **Log noise:** the expected 401/404 answers of `stale_device` and `unknown_link` are logged by `LoggingBehavior` as `fail` (Error) with a stack trace: 2 552 entries in this run. That predates L-3, but it will fill Loki with errors that are not errors.
