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
