# SS-SPORT Drive — Backlog

Ready to import into Jira: one **Epic** per section, one **Task** per row.
**Assignee:** `Owner` = you (accounts, keys, decisions, review). `Claude` = code. `Both` = done together (Claude explains, you approve, then build).
Every Claude task follows the working agreement: explain → approve → tests → implement (MediatR).

---

## EPIC 0 — Setup & accounts
| Key | Task | Assignee | Done when |
|---|---|---|---|
| S-1 | Pick and buy the VPS outside Iran (≈4 vCPU / 8 GB, Ubuntu LTS) | Owner | SSH access shared |
| S-2 | Cloudflare: add `ss-sport.co`, create R2 bucket + API token | Owner | Keys stored in the secret manager |
| S-3 | Google Cloud service account with Viewer access to the Shared Drive | Owner | Service-account JSON + Shared Drive ID available |
| S-4 | Create the Telegram bot with BotFather (`@sssportdrive_bot`) | Owner | Bot token available |
| S-5 | Resend + Brevo accounts, verify the `ss-sport.co` sending domain (SPF/DKIM/DMARC) | Owner | Test OTP mail lands in inbox |
| S-6 | Google SMTP app password for `info@ss-sport.co` | Owner | Test mail sent |
| S-7 | Cloudflare Turnstile site key | Owner | Site + secret key available |
| S-8 | Hand over brand files (logo variants) to `brand/` | Owner | Files in repo |
| S-9 | Create the Git repo, solution layout (Domain / Application / Infrastructure / Api / Workers + tests, `/web`, `/edge`, `/deploy`) | Claude | Solution builds, CI runs empty test suites |
| S-10 | docker-compose for local: PostgreSQL, RustFS (R2 stand-in; MinIO images are gone), API, workers, web | Claude | `docker compose up` works |
| S-12 | `.gitignore` + `.dockerignore` baselines (.NET, Node/Next.js, IDEs, secrets, env files) | Claude | Files committed |
| S-13 | Branching + commit conventions (`feature/<area>-<name>`, PR into main, protected main) | Both | Rules active on the repo |
| S-14 | Configuration: root `.env` loaded by DotNetEnv, CORS lockdown to the web origin, PostgreSQL connection + `/health/ready` | Claude | Tests green, stack reads `.env` |
| S-15 | Fix the Brevo API key (OTP fallback): key reactivated, API IP blocking off; done 2026-10-24 | Owner | Read-only Brevo check returns 200 |
| S-11 | Finalise watermark size/opacity and the overlay interval on real full-match footage | Both | Sample clip approved |

## EPIC 1 — Core domain & persistence
| Key | Task | Assignee | Done when |
|---|---|---|---|
| D-1 | Base building blocks: Entity, AggregateRoot, domain events, value objects | Both | Unit tests green |
| D-2 | Unit of Work + generic `IRepository<T>` (EF Core) | Both | Integration tests (Testcontainers) green |
| D-2b | Specification pattern base + evaluator wired into the generic repository | Both | Tests green |
| D-2c | OpenAPI + Scalar setup at `/scalar` (XML docs included, ProblemDetails, auth schemes, admin-only in production) | Claude | Scalar shows every endpoint with examples |
| D-3 | MediatR pipeline: validation, authorization, logging, transaction behaviours | Claude | Tests green |
| D-4 | Player aggregate: name rules, ISO nationality, positions (S/OH/OPP/MB/L/DS, max 2), Volleybox ID parsing, slug | Claude | Tests green |
| D-5 | Waiting-list state: completeness rules; Publish allowed only when complete | Claude | Tests green |
| D-6 | DriveFolder, Asset (tags from subfolder first, then file name), Rendition | Claude | Tests green |
| D-7 | User + roles (Analyst, Admin, extensible) + AuditLog | Claude | Tests green |
| D-8 | EF Core mappings + migrations | Claude | Migration applies on clean DB |
| D-9 | DTO pattern: request/response DTOs per endpoint, Mapperly mappers (replaced AutoMapper), entities never exposed | Claude | Mapping tests green; unmapped DTO members fail the build |
| D-10 | Countries lookup: the owner's 245-country list (ISO alpha-2 + XK, AC, TA) as the nationality catalogue, seeded into `countries` | Claude | Clean DB has all 245 countries; tests green |
| D-11 | Pagination building blocks (decisions §2b): `PageRequest` / `CursorRequest` DTOs + validators (size 20 default, 100 max), `PagedResult<T>` / `CursorResult<T>`, paging in specifications with a stable order, Scalar schemas; every list endpoint uses them | Claude | Tests green; no list endpoint returns an unbounded collection |
| D-12 | Fix the EF shadow-FK warning on the owned Volleybox profile (`VolleyboxProfile.PlayerId1`): explicit owner key on `players.id`, model test with warning 10625 as an error | Claude | No EF model warnings; tests green |
| D-13 | Concurrent edits answer 500: map optimistic concurrency conflicts to 409 (`concurrency.conflict`); log expected 4xx at Information/Warning, not Error (found by K-8) | Claude | Tests green |
| D-14 | Stop logging every SQL statement at Information: EF Core to Warning in the base config, then re-measure (found by K-9) | Claude | Tests green; owner approved the log levels |
| D-15 | Database connections run out under load: cap the Npgsql pool per process (or raise `max_connections` / add PgBouncer) so the API, workers and Hangfire stay under PostgreSQL's limit (found by D-14) | Claude | Owner chose the limits; tests green; k6 load shows no "too many clients" |

## EPIC 2 — Telegram bot & Mini App
| Key | Task | Assignee | Done when |
|---|---|---|---|
| T-1 | Webhook endpoint + secret validation | Claude | Tests green |
| T-2 | Access flow: request → admins approve/decline → welcome | Claude | E2E on test bot |
| T-2a | Fix red CI on `main`: `TelegramWebhookTests.Valid_update_is_recorded_once` fails without a bot token (the handler builds the real bot client); use a fake messenger in the test host | Claude | Test green in CI without `.env`; `main` green |
| T-3 | Mini App auth (initData signature, 24 h), `GET /miniapp/me`, `/miniapp` page, "Open Drive" menu button | Claude | Tests green |
| T-4a | New player form in the Mini App: category, name, nationality picker, positions incl. DS, Volleybox (live check), Drive folder link (format check); saved as a Waiting player owned by the analyst | Claude | Tests green |
| T-4b | Player photo upload (min 800 px) to R2 / RustFS through an object-storage port; shown back in the form | Claude | Tests green |
| T-4c | Drive folder live check with the service account: access, folder name, video/PDF count, total size | Claude | Tests green |
| T-4d | Player photo straight from the bot chat: reply to the player's progress message, or pick from the analyst's players without a photo (paged); same checks as T-4b | Claude | Tests green |
| T-5 | Update-player mode for a known Volleybox ID | Claude | Tests green |
| T-6a | One progress message per player, edited in place: links, folders, photo, missing details, waiting for an admin / published | Claude | Tests green |
| T-6c | Send progress messages after the response, not inside the request (found by K-9: player edit p95 929 ms, about 1 s per Telegram round trip) | Claude | Tests green; player edit p95 < 300 ms in k6 |
| T-6b | Build steps in the same message (copy, transcode, thumbnails, % and ETA) + result card + QR (needs Epic 3, admin Publish, short links) | Claude | Done (2026-11-13): build lines, bar with time left, result card and the QR sent once, driven by the asset events; the demo on real files waits for Epic 3 (I-2/I-3/I-4) and the QR for I-8 |
| T-7a | My players list (search, All / Live / Processing / Needs attention, paged) + player detail (card, folders, files in page order) + bot My players button | Claude | Demo |
| T-7b | Edit video bottom sheet: title, tags, set as hero; Hide/Show for admins only | Claude | Demo |
| T-8 | Notification rules: grouping, 10-min throttle, quiet hours 23:00–08:00 Tehran | Claude | Tests green |
| T-9 | Mini App UI: design every screen in the canvas (Telegram theme-aware, SS-SPORT accents), then style the screens without changing their logic (owner, 2026-10-31) | Both | Artboards approved; screens match; tests green |
| T-10 | Bot works by chat (owner, 2026-11-06): add a player step by step and see My players (paged, inline buttons) in the chat; the Mini App stays optional, offered by an Open button and the bot's main Mini App button. Chat flow designed with the owner first | Claude | A player can be created and listed entirely in the chat; tests green |

## EPIC 3 — Ingest, transcode & sync
| Key | Task | Assignee | Done when |
|---|---|---|---|
| I-1 | Drive reader: recursive listing of `SS-SPORT <Name>` folders and subfolders | Claude | Tests on fixtures |
| I-2 | Copy originals to R2 (reuse by md5) | Claude | Tests green |
| I-3 | ffmpeg HLS 360/720/1080, 6 s segments, burned-in watermark (logo top-left, slogan bottom-right) | Claude | Sample approved (S-11) |
| I-4 | Thumbnails, sprite, poster; PDF page count + preview images | Claude | Tests green |
| I-5 | Player card render (Barlow Condensed name, brand template) | Both | Card approved |
| I-6 | Sync poller every 10 min: new / renamed / replaced / removed / lost access | Claude | Tests per rule |
| I-7 | Background removal for player photos | Claude | Demo |
| I-9 | Ingest writes HLS renditions (I-3) and document page images (I-4) to the V-5b folders: `players/{player}/videos/{video}/v{version}/hls/` and `players/{player}/documents/{document}/v{version}/pages/{n}.webp` (found while planning V-5b) | Claude | A processed video streams and a processed PDF opens on a real personal link; tests green |
| I-8 | Player QR code: free in-house generation (QRCoder), SS-SPORT mark in the centre, PNG in R2, key on the player; opens the home search prefilled with the player's name | Claude | Every published player has a QR; tests green |

## EPIC 4 — Viewer site & media protection
| Key | Task | Assignee | Done when |
|---|---|---|---|
| V-1 | Next.js viewer app shell, fonts (Barlow Condensed / Geist / Geist Mono), brand tokens, motion system | Claude | Matches canvas |
| V-2 | Player page (desktop / tablet / mobile) per canvas | Claude | Playwright visual pass |
| V-2b | Player page on real data: viewer API for a link's players, assets and PDFs behind the viewer session; `/r/{code}` renders it after the gate (found while planning L-2) | Claude | Tests green; the gate opens the link's real players |
| V-3 | Media Chrome + hls-video-element player: speed, quality, frame step, no download, no context menu | Claude | Demo |
| V-4 | Moving viewer-email overlay (20 %, small, repositions every few seconds) | Claude | Demo |
| V-5a | Edge Worker: HMAC HLS tokens (30 min) bound to the viewer session cookie, R2 delivery, .NET token issuer, player background refresh (split 2026-10-31) | Claude | Copying the m3u8 elsewhere fails (Worker tests) |
| V-5b | HLS token endpoint on the viewer session + player wiring; short-link routing on the edge (after L-1/L-2) | Claude | Done (2026-11-12): signed stream and page URLs on the viewer session, edge `/pages` and short-link pass-through, player and document viewer sign on demand; the end-to-end check on a real link waits for I-9 |
| V-6 | View-only PDF viewer (rendered pages, no file download) | Claude | Demo |
| V-7 | Email-only contact CTA (`info@ss-sport.co`) | Claude | Done |
| V-8 | DataVolley (`.dvw`) viewer: in-page, view-only, no download (what it shows is designed with the owner first) | Claude | Done (2026-11-12): parsed match and per-skill stats in a view-only dialog (decisions §4a); design preview at `/design/scout` awaits the owner's review |

## EPIC 5 — Personal links, OTP & tracking
| Key | Task | Assignee | Done when |
|---|---|---|---|
| L-1 | Recipients + `/r/{code}` share links (expiry, pause, revoke) | Claude | Tests green |
| L-2 | Device binding + email gate + OTP (6 digits, 10 min, 5 tries) | Claude | Tests green |
| L-2b | Purge expired gate rows (email codes, viewer sessions, trusted devices) with a recurring Hangfire job; retention confirmed with the owner (found while building L-2) | Claude | Tests green |
| L-3 | Forwarding: any OTP-verified email, LinkForwarded alert, Keep/Block | Claude | Tests green |
| L-3b | Email the viewer when an admin keeps their forwarded link (found while building L-3); never on Block; needs L-4's email queue | Claude | Tests green; Keep sends one email, Block none |
| L-4 | Email queue on Hangfire: Resend → Brevo failover for OTP, Google SMTP for the rest, retries, admin view | Claude | Failover test green |
| L-5 | Tracking events + heartbeats + sessions | Claude | Tests green |
| L-6 | Metrics: unique watched (range merge), watch time, plays, hotspots, completion | Claude | Unit test per metric |
| L-7 | Admin bot alerts + daily digest 09:00 Tehran | Claude | Part done (2026-11-12): first-open alert and the digest with sessions, forwards and the waiting list; plays, minutes, top players and failed emails (7b) wait for L-4/L-5/L-6 |
| W-2 | Player sends the watch start, a heartbeat every 10 s and play/pause/seek/ended to the tracking API (L-5 client part, in the V-3 player) | Claude | Tests green |
| W-3 | Retention and clean-up for `outgoing_emails`, `tracking_events`, `sign_in_links` and `account_sessions` (owner to decide the periods) | Both | Tests green |

## EPIC 6 — Public search & access requests
| Key | Task | Assignee | Done when |
|---|---|---|---|
| P-1 | Public home: player search + position filter | Claude | Matches canvas |
| P-2 | Locked player page: blurred / locked media, no real media URLs sent | Claude | Security test green |
| P-3 | Request-access flow: email + description, OTP, Turnstile, rate limits | Claude | Tests green |
| P-4 | Admin access-request inbox (console + bot): approve → auto personal link + approval email | Claude | Done (2026-11-14): bot alert with Approve / Decline, console API (list, approve with changes, decline), personal link made and emailed; the console page is A-12 |
| P-5 | Viewer login by email magic link (1-day session) | Claude | Tests green |
| P-7 | Home page login button; signed-in viewers request access without email/OTP | Claude | Done (2026-11-14): Sign in leads to /sign-in and the session routes exist (W-1) |
| P-8 | Backend half of P-1, P-2, P-3 and P-7: public player search + locked player API, request-access API (OTP, Turnstile, rate limits), `/api/public/*` and sign-out routes, real `/players/{slug}` page, real Turnstile widget + CSP. The front-end of all four is done on sample data (`/design/home`, `/design/locked`) | Claude | Tests green; the public pages run on real data |
| W-1 | Web part of sign-in: `/admin/sign-in` and `/sign-in` pages, same-origin routes that set `__Host-ssd_as` / `__Host-ssd_va` and forward `X-Admin-Session` / `X-Viewer-Account`, home page Sign in / Sign out (A-1 and P-5 web part, needed by P-7) | Claude | Done (2026-11-14): /sign-in and /admin/sign-in with a Continue button, same-origin session and sign-out routes, `__Host-ssd_acct` / `__Host-ssd_as` cookies |

## EPIC 7 — Admin console
| Key | Task | Assignee | Done when |
|---|---|---|---|
| A-1 | Admin login by email magic link (7-day session) + CSRF | Claude | Tests green |
| A-2 | Dashboard (KPIs, live activity, needs attention, top players) | Claude | Demo |
| A-3 | Players list + search + filters in URL + CSV export | Claude | Demo |
| A-4 | Player detail: files (order, hero, visibility, retry), folders, links | Claude | Demo |
| A-5 | **Waiting list:** complete-details drawer, Publish disabled until complete | Claude | Matches canvas |
| A-6 | New share link modal (no download toggle) | Claude | Demo |
| A-7 | Viewer profile + replay heatmap | Claude | Demo |
| A-8 | Users & roles + audit log | Claude | Demo |
| A-9 | Sync & jobs + Email queue pages | Claude | Demo |
| A-10 | Settings (contact email, digest time, quiet hours, link expiry default) | Claude | Demo |
| A-11 | Viewer access & watch log: per viewer players accessible, plays, minutes, last seen, expiry, per-video detail | Claude | Matches canvas |
| A-12 | Access-requests console page on the P-4 API: paged list with status filter, approve with changes (expiry, players, note), decline (found while planning P-4: the console shell does not exist yet) | Claude | Tests green |

## EPIC 8 — Archive migration
| Key | Task | Assignee | Done when |
|---|---|---|---|
| M-1 | Scan the Shared Drive (~100 GB), match to the R2 backup by md5 | Claude | Report generated |
| M-2 | Suggestions from folder names (`SS-SPORT <Name>`) and subfolders; incomplete players go to the waiting list | Claude | Tests green |
| M-3 | CSV export/import round-trip | Claude | Demo |
| M-4 | Throttled transcode queue (new uploads first) | Claude | Demo |
| M-5 | Fill in missing Volleybox links / photos for the archive | Owner | Waiting list empty |

## EPIC 9 — Deploy & operations
| Key | Task | Assignee | Done when |
|---|---|---|---|
| O-1 | VPS provisioning: Docker, firewall, TLS, backups for PostgreSQL | Both | Runbook written |
| O-2 | CI pipeline: build, lint, unit + integration tests (Testcontainers), Docker image build on every PR | Claude | PR checks required on main |
| O-3 | CD pipeline: auto-deploy main to staging; tagged release to production with approval; migrations step; rollback | Claude | One-click deploy + documented rollback |
| O-4 | Serilog structured logging + enrichers (correlation ID, user, player) with redaction of secrets, OTPs and emails | Claude | Tests on redaction green |
| O-5 | Grafana Loki + Grafana on the VPS; ship API, worker and bot logs; base dashboards + alerts | Both | Dashboards live |
| O-6 | Health checks, failed-job alerts to admins via the bot | Claude | Demo |
| O-7 | Security checklist pass (brief §12) | Both | Checklist signed off |
| O-8 | Full penetration test before the final production deploy. First map the attack surface with **/graphify** (knowledge graph of the whole codebase: endpoints, auth paths, data flows, sensitive nodes), then test: OWASP Top 10 / ASVS L2 against staging (auth, OTP, personal links, HLS token, IDOR, admin console, bot/Mini App, rate limits, headers/TLS, Docker/VPS hardening), automated (OWASP ZAP, dependency + container scans) plus manual testing; every Critical/High fixed and re-tested | Both | Report signed off, no open Critical/High; production deploy is blocked until then |
| O-9 | Security baseline (decisions §1b): HTTPS/HSTS, API + web security headers with CSP nonce, rate limiting with suspicious-activity logs, least-privilege DB role. Admin lock, session cookies and CSRF land with A-1 / P-5 | Claude | Tests green |
| O-10 | Pre-pentest hardening (decisions §1b, 2026-11-13): Admin policy on every `/admin/*` endpoint, mandatory share-link expiry, session lifetimes admin 30 d / viewer 7 d, validators for every input-carrying command/query, CI secret + dependency scans | Claude | Tests green |

## EPIC 10 — Performance (k6)
| Key | Task | Assignee | Done when |
|---|---|---|---|
| K-1 | k6 project setup + smoke test in CI against staging | Claude | Smoke runs on every deploy |
| K-2 | Benchmark the viewer player page + HLS token refresh | Both | Report reviewed |
| K-3 | Benchmark the tracking heartbeat ingest | Both | Report reviewed |
| K-4 | Benchmark the public search + request-access OTP endpoints | Both | Report reviewed |
| K-5 | Benchmark the admin console APIs (players list, viewer analytics) | Both | Report reviewed |
| K-6 | Benchmark the Mini App API: sign-in, countries, Volleybox check, create player (`perf/k6/miniapp.js`) | Both | Report reviewed |
| K-7 | Benchmark the Mini App photo upload/read and the live Drive folder check (same script, opt-in scenarios) | Both | Report reviewed |
| K-8 | Benchmark the Mini App update mode: player card and details edit (same script, opt-in scenarios) | Both | Report reviewed |
| K-9 | Benchmark My players, the player files and the edit-video sheet (same script, opt-in scenarios); fix the Volleybox id window and the run recipe | Both | Report reviewed |
| K-10 | Email-code requests slow the whole API under load: with about 1 code request a second next to ~100 viewer reads a second, every request goes from tens of milliseconds to seconds and single-row lookups log as slow SQL (found by the V-2b k6 run, `perf/k6/viewer-links.js`). Find the cause (blocking Hangfire enqueue, thread-pool starvation, locks) and fix it | Claude | Done (2026-11-12): did not reproduce on a clean re-run; peak 20 and peak 50 with `SEND_CODES=1` pass every threshold (`perf/k6/README.md`) |
| K-11 | Telegram Bot API stand-in for k6 runs, so the webhook benchmark measures our own code (found by the T-8 benchmark: webhook `new_updates` p95 364 ms because each `/start` waits on a real api.telegram.org call); bench-only setting, refused in production; rerun webhook + Mini App load on a quiet machine | Claude | Tests green; webhook load passes its thresholds against the stand-in; results in `perf/k6/README.md` |
