# Plan: the admin console (Epic 7: A-2 to A-11, plus the admin half of W-1)

Written 2026-11-14 (Opus) for an implementing model (Sonnet 5.5). Branch `feature/admin-console`, cut from `main` at
`b79da4f`. **The tests are already written and committed red.** Your job is to make every test green without weakening
any of them, following the phases below in order. This closes **Epic 7**.

| Backlog | Jira | Phase |
|---|---|---|
| W-1 (admin half only) | SSD-133 (stays open for the viewer half) | 0 |
| A-2 Dashboard | SSD-69 | 2 |
| A-3 Players list + CSV | SSD-70 | 3 |
| A-4 Player detail | SSD-71 | 4 |
| A-5 Waiting list | SSD-72 | 5 |
| A-6 New share link modal | SSD-73 | 6 |
| A-7 Viewer profile + heatmap | SSD-74 | 7 |
| A-11 Viewer access & watch log | SSD-98 | 7 |
| A-8 Users & roles + audit | SSD-75 | 8 |
| A-9 Sync & jobs + email queue | SSD-76 | 9 |
| A-10 Settings | SSD-77 | 1 (backend) + 10 (page) |

Owner decisions behind this plan are in `.claude/decisions.md` §12 "Admin console". Read it first. Also read §7b (team
users, roles, audit), §5 (waiting list, publish), §8a (sign-in, tracking metrics) and §2b (pagination).

---

## 0. Before you start

- Read `.claude/CLAUDE.md`. Everything in it applies:
  - MediatR for every command and query.
  - The Specification pattern for query conditions, and `AsNoTracking` projections on reads.
  - Mapperly for DTOs.
  - XML doc comments on **every** member, public and private, tests too. TSDoc on every exported TS function and component.
  - English only. UI copy goes in `copy.ts` files.
  - Source-generated Serilog `[LoggerMessage]` logging that never logs an email, token or code.
  - Every list paged in SQL.
  - Complete Scalar docs for every endpoint.
  - The security rules (§1b).
- **Never edit a test to make it pass.** If a test truly contradicts `decisions.md`, stop and ask the owner. One
  exception: the two test-support files (`Support/BackendTestHost.Admin.cs` and the `services` parameter added to
  `BackendTestHost.StartAsync`) never ran, because Docker was off when they were written. If a helper itself is broken
  (not an assertion), fix the helper, keep its intent, and list the fix in the PR.
- Values marked **(proposed)** are Opus's proposals and are what the tests expect. List them in the PR description so
  the owner can confirm them.
- Jira (site `bonyadin2000.atlassian.net`, project SSD): move each key to *In Progress* when you start its phase. Once
  the phase is green and committed, comment on it (branch, commit, test result) and move it to *Done*.
- Every `/admin/*` endpoint calls `.RequireAdmin()`. `AdminAuthorizationTests` enumerates them, so a missing call fails CI.
- Every command or query with input has a FluentValidation validator (`ArchitectureTests/ValidationTests`). Every
  list is an `IPagedQuery`/`ICursorQuery` (`ArchitectureTests/PagingTests`). DTOs never expose entities (`DtoBoundaryTests`).

### Closest models to copy

| What | Copy from |
|---|---|
| Admin endpoints, Scalar docs, `RequireAdmin`, examples | `src/SsSport.Drive.Api/Links/ShareLinkEndpoints.cs` |
| Admin cursor list + enum filter → 400 | `src/SsSport.Drive.Api/Emails/EmailQueueEndpoints.cs` |
| One SQL projection for a multi-aggregate list (recorded exception) | `src/SsSport.Drive.Infrastructure/Persistence/PlayerListReader.cs` (`IPlayerListReader`) |
| Viewing reads per viewer/video (L-6 metrics) | `IViewingReader` + `GetLinkViewingQuery` in `src/SsSport.Drive.Application/Tracking/Tracking.cs` |
| File edit + audit | `EditPlayerFileCommand` in `src/SsSport.Drive.Application/MiniApp/EditPlayerFile.cs` |
| Player details update | `UpdatePlayerDetailsCommand` / `UpdatePlayerDto` in `src/SsSport.Drive.Application/MiniApp/UpdatePlayer.cs` |
| Approve / decline + audit | `src/SsSport.Drive.Application/Telegram/Access/AccessCommands.cs` |
| Analyst notice on publish | already exists: `PlayerPublishedNotice` (`Telegram/Notices`), runs on `PlayerPublished` |
| Ingest after publish / retry | `IIngestScheduler`, `IngestPublishedPlayer` in `Application/Ingest/IngestTriggers.cs` |
| Recurring Hangfire job | `RegisterRecurringJobs` in `Infrastructure/BackgroundJobs/BotNotices.cs` |
| Notification rules | `INotificationSettings` (`Application/Telegram/Notices/BotNotices.cs`), `NotificationOptions` (`Infrastructure/Telegram`) |
| Aggregate with a time-ordered id and an event | `src/SsSport.Drive.Domain/Links/LinkForward.cs` |
| EF configuration + migration | `Infrastructure/Persistence/Configurations/ViewerGateConfiguration.cs`, `dotnet ef migrations add` |
| Same-origin web route, cookies, CSRF | `web/src/lib/viewer/public-server.ts` (`signOut`), `gate-server.ts` (`isSameOrigin`, `readCookie`, `problem`) |
| Dialog behaviour (focus, Escape, scroll lock) | `web/src/components/viewer/document-viewer/document-viewer.tsx` |

### Test files (all red now)

| Phase | Test file | Why red now |
|---|---|---|
| 0 | `web/src/lib/admin/session.test.ts`, `api.test.ts` | modules missing |
| 0 | `web/src/components/admin/shell/admin-nav.test.tsx` | component missing |
| 0 | `web/src/lib/admin/format.test.ts` | module missing |
| 1 | `tests/SsSport.Drive.Domain.UnitTests/Settings/AppSettingsTests.cs` | `Domain.Settings` missing |
| 1 | `tests/SsSport.Drive.Api.IntegrationTests/Admin/AdminSettingsTests.cs` | `IDigestSchedule`, endpoints missing |
| 2 | `.../Admin/AdminDashboardTests.cs` | 404 |
| 3 | `.../Admin/AdminPlayersTests.cs`, `web/src/lib/admin/players-query.test.ts` | 404 / module missing |
| 4 | `.../Admin/AdminPlayerDetailTests.cs` | 404 |
| 5 | `.../Admin/WaitingListTests.cs`, `web/src/lib/admin/waiting.test.ts`, `web/src/components/admin/waiting/complete-details.test.tsx` | 404 / missing |
| 6 | `web/src/components/admin/links/new-link-modal.test.tsx` | component missing |
| 7 | `tests/SsSport.Drive.Domain.UnitTests/Viewers/ViewerTests.cs`, `.../Admin/AdminViewersTests.cs`, `web/src/components/admin/viewer/replay-heatmap.test.tsx` | missing / 404 |
| 8 | `.../Admin/AdminUsersTests.cs` | 404 |
| 9 | `tests/SsSport.Drive.Domain.UnitTests/Emails/OutgoingEmailRetryTests.cs`, `.../Admin/AdminJobsTests.cs` | `Retry` missing / 404 |
| 10 | `web/src/lib/admin/settings-form.test.ts` | module missing |

C# test projects compile as a whole: nothing in a project runs until **every** type it references exists. So:

### 0.1 Stubs first (one commit, no behaviour)

Create these with `throw new NotImplementedException()` bodies, so `dotnet build SsSport.Drive.slnx` is clean. Match the
tests exactly:

- `Domain/Settings/AppSettings.cs`: `AppSettings : AggregateRoot<Guid>`, `static readonly Guid SingletonId`,
  `static IReadOnlyList<int> LinkExpiryChoices` (`[7, 30, 90]`),
  `CreateDefault(EmailAddress contactEmail, TimeOnly digestAt, TimeOnly quietStart, TimeOnly quietEnd, int defaultLinkExpiryDays, DateTimeOffset now)`,
  `IReadOnlyList<string> Update(EmailAddress, TimeOnly, TimeOnly, TimeOnly, int, Guid? actorUserId, DateTimeOffset now)`,
  the properties `ContactEmail`, `DigestAt`, `QuietHoursStart`, `QuietHoursEnd`, `DefaultLinkExpiryDays`, `UpdatedAt`,
  `UpdatedByUserId`, and the event `sealed record AppSettingsChanged(IReadOnlyList<string> ChangedFields) : IDomainEvent`.
- `Domain/Viewers/Viewer.cs`: `Viewer : AggregateRoot<Guid>`, `static Viewer Register(EmailAddress email, DateTimeOffset now)`
  (id `Guid.CreateVersion7(now)`), `Email`, `FirstSeenAt`.
- `OutgoingEmail.Retry(DateTimeOffset now)`.
- `Application/Settings/IDigestSchedule.cs`: `public interface IDigestSchedule { void Apply(TimeOnly digestAt); }`.

Commit: "Add stubs for the admin console".

### Running tests

```bash
dotnet test --project tests/SsSport.Drive.Domain.UnitTests
dotnet test --project tests/SsSport.Drive.Api.IntegrationTests -- --filter-namespace "SsSport.Drive.Api.IntegrationTests.Admin"
dotnet test --project tests/SsSport.Drive.Api.IntegrationTests -- --filter-class "SsSport.Drive.Api.IntegrationTests.Admin.AdminSettingsTests"
dotnet test --project tests/SsSport.Drive.ArchitectureTests
cd web && npx vitest run src/lib/admin src/components/admin && npm run typecheck && npm run lint
```

Integration tests need Docker running (Testcontainers PostgreSQL). At the end of every phase, run the **whole** suite
(`dotnet test` on the solution, plus `cd web && npx vitest run`), not just the phase's tests.

---

## Shared conventions for every console endpoint

- Route prefix `/admin/…`, tag per area (`Admin dashboard`, `Admin players`, `Admin viewers`, `Admin users`,
  `Admin jobs`, `Admin settings`), `.RequireAdmin()`, and requests that implement `IAuthorizedRequest` with
  `RequiredRoles => ShareLinkRules.AdminRoles` (the second check).
- Page lists use `[AsParameters] PagingParameters` → `PagedResult<T>` (`page`, `pageSize`, `totalCount`, `totalPages`,
  `items`). Feeds that are "newest first, forever growing" use `CursorParameters` → `CursorResult<T>` (`items`,
  `nextCursor`). The page size is capped at 100 (400 above it).
- Enum-like query filters are parsed case-insensitively; an unknown value is `400` ValidationProblem keyed by the
  parameter name (copy `EmailQueueEndpoints.ListAsync`).
- Viewer emails are shown in full **only** in the viewer endpoints (admins see identities, decisions §3a). Team member
  emails are masked (`EmailAddress.ToString()`). Audit data never holds an email (`audit.data_contains_email`).
- Each new read that spans aggregates gets a reader port in `Application/Abstractions/Persistence` with an EF Core
  implementation of one SQL projection (as `IPlayerListReader`). Record each as a §2 exception in `decisions.md` in one line.
- Times in JSON are ISO `DateTimeOffset` (UTC). "Today" and day buckets use `Asia/Tehran` (resolve it like
  `NotificationOptions.ResolveTimeZone`: fall back to a fixed +03:30 zone when the OS has no tz data).

---

## Phase 0: web sign-in, console shell and API client (W-1 admin half)

Tests: `web/src/lib/admin/session.test.ts`, `api.test.ts`, `format.test.ts`, `components/admin/shell/admin-nav.test.tsx`.

### 0.2 `web/src/lib/admin/session.ts` (server only)

- `ADMIN_COOKIE = "__Host-ssd_as"`, `ADMIN_HEADER = "X-Admin-Session"`.
- `requestAdminLink(request, apiBaseUrl, fetchImpl = fetch)`: the handler for `POST /api/admin/sign-in-link`.
  - Not same origin → 403. Not `application/json` → 415. Neither reaches the API.
  - Otherwise forward `{ email }` to `POST {api}/auth/admin/link` with JSON and the visitor's `X-Forwarded-For` (reuse
    `clientAddress`).
  - Answer 202 for any API answer except 429, which passes through with its `Retry-After`.
- `startAdminSession(request, apiBaseUrl, fetchImpl)`: the handler for `POST /api/admin/session`. It takes a form body
  with `token`.
  - Not same origin → 403.
  - Empty token, or the API refuses it → `303 Location: /admin/sign-in?error=link` with no cookie.
  - Otherwise `POST {api}/auth/admin/session` with `{ token }`. On 200, answer `303 Location: /admin` with
    `Set-Cookie: __Host-ssd_as=<sessionToken>; Path=/; Max-Age=<seconds until expiresAt>; HttpOnly; Secure; SameSite=Lax`
    (no `Domain`).
- `adminSignOut(request, apiBaseUrl, fetchImpl)`: the handler for `POST /api/admin/sign-out`. Copy `signOut` from
  `public-server.ts`: it posts `{}` to `/auth/admin/sign-out` with `X-Admin-Session`, clears the cookie and redirects
  `303` to `/admin/sign-in`. A cross-site request → 403.
- `readAdmin(apiBaseUrl, token | undefined, fetchImpl)` → `{ email, displayName, expiresAt } | null`. It calls
  `GET /auth/admin/me` with `cache: "no-store"`. No token, any error status or a network failure → null.

### 0.3 `web/src/lib/admin/api.ts` (server only)

- `class AdminApiError extends Error { status: number; code: string | null }`.
- `adminFetch<T>(apiBaseUrl, token, path, init?, fetchImpl = fetch): Promise<T | null>`. It sends `X-Admin-Session`,
  `cache: "no-store"`, and `Content-Type: application/json` when there is a body. A 204 answers null; JSON is parsed;
  a non-2xx answer throws `AdminApiError(status, problem.code)`.
- `forwardAdmin(request, apiBaseUrl, segments: string[], fetchImpl)`: the handler for `app/api/admin/[...path]/route.ts`
  (GET, POST, PUT, PATCH, DELETE).
  1. Not same origin → 403.
  2. A write without `application/json` → 415.
  3. No `__Host-ssd_as` cookie → 401.
  4. A segment that is empty, `.` or `..` (also after `decodeURIComponent`), or that contains `/` or `\` → 404. These
     never reach the API.
  5. Otherwise forward to `{api}/admin/{segments.join("/")}{search}` with `X-Admin-Session` and the body, and pass the
     status, `Content-Type`, `Content-Disposition` and body back with `Cache-Control: no-store`.
  6. A network failure → 502.
- Keep the specific `/api/admin/session`, `/sign-in-link` and `/sign-out` routes as separate files; they win over the
  catch-all.

### 0.4 `web/src/lib/admin/format.ts`

`formatWatchTime`, `formatRelative(at | null, now)`, `formatPercent(ratio | null)`, `sessionDaysLeft(expiresAt, now)`, as
the test tables say. Dates older than a week are written `d MMM` in `Asia/Tehran`.

### 0.5 Shell: pages and nav

- `web/src/app/admin/sign-in/page.tsx`: the canvas `AdminLogin`.
  - Without `?token`: the email form, posted with `fetch` to `/api/admin/sign-in-link`. Then the "check your email"
    step, with a back button.
  - With `?token`: a single **Continue** form that POSTs `token` to `/api/admin/session` (decisions §12: a mail
    scanner must not use up the link).
  - `?error=link`: "This sign-in link has expired or was already used." plus the email form.
- `web/src/app/admin/(console)/layout.tsx`: reads the cookie, calls `readAdmin`, and `redirect("/admin/sign-in")` when
  it is null. It renders `AdminNav` and `<main>`. The nav counts come from `GET /admin/waiting/summary` (`waiting`) and
  `GET /admin/dashboard` (`kpis.pendingAccessRequests`). Pages use `export const dynamic = "force-dynamic"`. Add
  `X-Robots-Tag: noindex` on `/admin/*` in `next.config.ts` headers.
- `web/src/components/admin/shell/admin-nav.tsx`: `AdminNav({ current, counts: { waiting, accessRequests }, admin, now })`.
  - The section ids and hrefs are in the test.
  - `/admin/requests` is P-4's page (Epic 6). Until P-4 lands, the route renders a short placeholder that says the
    inbox is coming, with a link to the dashboard.
  - Sign out is `<form method="post" action="/api/admin/sign-out">`.
  - Copy every size, colour and motion from the canvas boards (`project/Admin*.dc.html` in
    https://claude.ai/artifact/PQ2gDk8gUoxkTyayfvVZe9; read them with the Artifact read action). All boards share one
    aside and one header block. Build them once as `AdminShell` and `AdminPageHeader` (eyebrow "ADMIN CONSOLE", h1,
    lead).
- Copy goes in `web/src/components/admin/copy.ts`.

---

## Phase 1: settings backend (A-10)

Tests: `AppSettingsTests`, `AdminSettingsTests`.

### 1.1 Domain `Domain/Settings/AppSettings.cs`

- One row on `SingletonId` (`00000000-0000-0000-0000-000000000001`).
- `Update(...)`:
  - Returns the camelCase names of the changed fields in this order: `contactEmail`, `digestAt`, `quietHoursStart`,
    `quietHoursEnd`, `defaultLinkExpiryDays`.
  - Raises one `AppSettingsChanged(changed)` only when something changed.
  - Sets `UpdatedAt`/`UpdatedByUserId` only on a change.
- Rules: the default expiry is in `LinkExpiryChoices`, else `DomainException("settings.invalid_link_expiry")`. Times
  are whole minutes (seconds and below are 0), else `settings.invalid_time`. `CreateDefault` checks the same rules.
- DRY: make `ShareLinkRules.ExpiryDays` return `AppSettings.LinkExpiryChoices`.

### 1.2 Persistence

- Table `app_settings`: `contact_email`, `digest_at` (time), `quiet_hours_start`, `quiet_hours_end`,
  `default_link_expiry_days`, `updated_at`, `updated_by_user_id`. Add a migration.
- `IAppSettingsStore` (Application port): `Task<AppSettings> GetAsync(CancellationToken)`. When there is no row, it
  returns `CreateDefault` from configuration:
  - contact email: `Settings__ContactEmail`, default `info@ss-sport.co` **(proposed)**;
  - digest: `Settings__DigestAt`, default `09:00` **(proposed)**;
  - quiet hours: the existing `Notifications__QuietHoursStart` and `Notifications__QuietHoursEnd`;
  - default expiry: `30` **(proposed)**.

  The row is inserted on the first save, not on read.

### 1.3 Application `Application/Settings/Settings.cs`

- `SettingsDto`: `contactEmail`, `digestAt` "HH:mm", `quietHoursStart`, `quietHoursEnd`, `defaultLinkExpiryDays`,
  `linkExpiryChoices` `[7,30,90]`, `timeZone` "Asia/Tehran", `updatedAt`.
- `GetSettingsQuery`.
- `UpdateSettingsCommand(contactEmail, digestAt, quietHoursStart, quietHoursEnd, defaultLinkExpiryDays)`.
  - Validator: the email parses as `EmailAddress`; the times match `^([01]\d|2[0-3]):[0-5]\d$`; the expiry is in the
    choices. Errors are keyed by the camelCase field names (the test reads `errors.<field>`).
  - Handler: load or create the row, then `Update`. On a change, add the audit entry `settings.changed` with target
    `AppSettings` and data `{"fields":[...]}`. When `digestAt` changed, call `IDigestSchedule.Apply(digestAt)`.
- `INotificationSettings.Rules` must now read the stored quiet hours. Make the Infrastructure implementation read
  `IAppSettingsStore`, with throttle and time zone still from configuration. Cache the row in `IMemoryCache` for 1 minute
  **(proposed)** and evict it in the update handler, so the test's next scope sees the new hours at once. Mind the
  lifetime: `INotificationSettings` is used by singletons today. Either make it scoped and adjust its consumers, or keep
  it a singleton that reads through `IServiceScopeFactory`. Run the T-8 notice tests (`BotNoticeRulesTests`,
  `BotNoticeQueueTests`) after this.
- `IDigestSchedule` (Infrastructure: `HangfireDigestSchedule`): `RecurringJobManager.AddOrUpdate<DailyDigestJob>(…,
  Cron.Daily(h, m), new RecurringJobOptions { TimeZone = tehran })`. `RegisterRecurringJobs` must use the stored
  digest time at start-up instead of the fixed `Cron.Daily(5, 30)`.
- Contact email on the player page: add `ContactEmail` to `LinkContentDto` (filled from `IAppSettingsStore`), and use
  it on the web player page instead of the constant (`V-7` contact CTA).

### 1.4 API `Api/Settings/SettingsEndpoints.cs`

`GET /admin/settings` (`GetSettings`), `PUT /admin/settings` (`UpdateSettings`, 200 with the new `SettingsDto`, 400).

---

## Phase 2: dashboard (A-2)

Test: `AdminDashboardTests`.

- `GET /admin/dashboard?period=7d|30d|90d` (default `30d`; anything else is 400). It answers:
  - `period`.
  - `kpis`:
    - `livePlayers`: published players.
    - `views`: watch sessions started in the period.
    - `previousViews`: the same count for the period before it.
    - `uniqueViewers`: distinct watch emails in the period.
    - `averageCompletion`: the mean of `WatchSession.Completion` over the period's watches, null when there are none.
    - `pendingAccessRequests`: `AccessRequestStatus.Pending`.
    - `waitingPlayers`: not published.
  - `viewsByDay`: exactly N entries `{date "yyyy-MM-dd", views}`, oldest first, ending today in Tehran, with zeros
    filled in.
  - `needsAttention`: `{accessRequests, waitingPlayers, failedFiles, failedEmails, retryingEmails (Queued with Attempts > 0), lostFolders}`.
  - `topPlayers`: at most 5 `{playerId, name, views, averageCompletion}`, ordered by views and then name. This is a
    fixed top-N, not a list, so it is not paged.
- One `IDashboardReader` with a few aggregate SQL queries. Nothing is loaded whole: use `GROUP BY` and `COUNT` in SQL.
  Completion is stored per watch as ranges (JSON), so either add a `completion` column to `watch_sessions` that is
  updated on every heartbeat (**proposed**, migration + backfill) or compute it in SQL. Do not load the watches into
  memory.
- `GET /admin/activity?cursor&pageSize`: `CursorResult<ActivityRowDto>`, ordered by `LastSeenAt` descending.
  `ActivityRowDto` has `watchId`, `viewerId`, `email`, `organisation` (the recipient's), `playerId`, `playerName`,
  `videoTitle`, `completion` and `isLive`. `isLive` means `LastSeenAt` within the last 60 s **(proposed)**.
- Web `web/src/app/admin/(console)/page.tsx`: the canvas `AdminDashboard`. The period chips live in the URL
  (`?period=`). The KPI cards link as on the canvas. The bar chart comes from `viewsByDay`. Each needs-attention row
  shows only when its count is > 0.

---

## Phase 3: players list + CSV (A-3)

Tests: `AdminPlayersTests`, `web/src/lib/admin/players-query.test.ts`.

- `GET /admin/players?q&position&status&page&pageSize` → `PagedResult<AdminPlayerRowDto>`.
  - Row fields: `id`, `name`, `positions` (codes `S/OH/OPP/MB/L/DS`), `nationalityName`, `videoCount`,
    `documentCount`, `viewers` (distinct watch emails, all time), `lastViewedAt` (the latest `LastSeenAt` of its
    watches), `analystName`, `status`, `hasPhoto`.
  - `status` is one of `Live|Processing|Attention|Waiting`, defined in the test class summary.
  - Sorting is as `PlayerListReader`.
- Extend `IPlayerListReader`/`PlayerListCriteria` (add `Position`, the console statuses and the admin columns), or add
  an `IAdminPlayerListReader` that reuses its SQL (DRY: one SQL text, one filter builder).
- Search: keep `PlayerListReader`'s rule (name `ILIKE`, or the exact Volleybox id when the text is a number). Searching
  by folder name too is **(proposed)** and has no test.
- `GET /admin/players.csv` with the same filters:
  - `text/csv; charset=utf-8`, `Content-Disposition: attachment; filename="players-yyyy-MM-dd.csv"` (Tehran date),
    `Cache-Control: no-store`, CRLF line endings.
  - Columns are exactly as in the test.
  - **Stream it**: page through the reader 500 rows at a time and write to the response. Never build the whole table in
    memory (decisions §2b). The export is an explicit admin download.
  - Quote a cell that contains `,` `"` CR or LF. Prefix a cell that starts with `= + - @` TAB or CR with `'` (CSV
    injection).
  - Exempt this endpoint from `PagingTests` the same way other non-list endpoints are; read that test.
- Web `web/src/lib/admin/players-query.ts`: as the test says. Page `web/src/app/admin/(console)/players/page.tsx`
  follows the canvas `AdminPlayers`. The search input updates the URL (debounced 300 ms, `router.replace`). The chips
  use `withFilter`. Export CSV is a plain `<a href={playersCsvHref(query)} download>`. There are page controls. A row
  links to `/admin/players/{id}`.

---

## Phase 4: player detail (A-4)

Test: `AdminPlayerDetailTests`.

- `GET /admin/players/{id}` → `AdminPlayerDto`, 404 when unknown. It reports:
  - `id`, `name`, `slug`, `positions` (codes), `nationality {code,name}`, `volleyboxId`, `volleyboxUrl`, `photoUrl`
    (null if none), `analystId`, `analystName`, `createdAt`, `publishedAt`, `status` (the console status), `missing`
    (`MissingDetails()` names), `heroAssetId`;
  - `viewersWithAccess`: distinct emails that watched or are recipients of an open link to this player;
  - `averageCompletion`;
  - `folders` (≤ 20, newest first; reuse `PlayerFoldersSpec`) `{id,name,lastSyncedAt,access}`.
- `GET /admin/players/{id}/files?page&pageSize` → `PagedResult<AdminPlayerFileDto>`. That is `PlayerFileDto` plus
  `source`, `removedFromSource`, `failureReason` and `fileName`. Use page order (`PlayerFilesInPageOrderSpec`).
- `PATCH /admin/players/{id}/files/{fileId}` with `EditPlayerFileDto`. Send the existing `EditPlayerFileCommand`
  (admins pass `PlayerAccess.EnsureCanManage`) and answer the admin file DTO.
- `POST /admin/players/{id}/files/{fileId}/move` with `{ position }` (1-based) → 204.
  - `MoveFileCommand`. The validator checks `position ≥ 1`. In the handler, `position` > the file count is
    `RequestValidationException` → 400. Another player's file → 404.
  - Renumber the moved file and the files between its old and new place (`Asset.MoveTo`), in one transaction.
  - Audit `asset.moved` **(proposed)**.
- `POST /admin/files/{id}/retry` → 202.
  - It calls `Asset.Retry()`; any state other than Failed is a 422 `asset.invalid_state` (already the domain rule).
  - Then it schedules the asset's next ingest step with `IIngestScheduler` (copy what `IngestPublishedPlayer` queues
    for one asset).
  - Audit `asset.retried` **(proposed)**.
  - Phase 9 reuses it.
- Web `web/src/app/admin/(console)/players/[id]/page.tsx` follows the canvas `AdminPlayer`, with the tabs Files and
  "Who has access · N":
  - Files: drag to reorder (one `move` call per drop, keyboard alternative: Move up/down buttons), a hero star, a
    visibility switch (`role="switch"`), and Retry on failed rows.
  - Who has access: `GET /admin/viewers?playerId=` and a link to `/admin/viewers/{id}`.
  - The aside: the Drive folder and the share links from `GET /admin/links?playerId=`, with pause, resume and revoke
    through the existing endpoints.
  - The Activity tab of the canvas is **out of scope** (not in A-4; a follow-up). Do not build it.
  - "+ Share link" opens the Phase 6 modal; `?newLink=1` opens it on load.

---

## Phase 5: waiting list (A-5)

Tests: `WaitingListTests`, `web/src/lib/admin/waiting.test.ts`, `complete-details.test.tsx`.

- `GET /admin/waiting?missing=name|nationality|positions|volleybox|photo&page&pageSize`. Only unpublished players,
  ordered by `CreatedAt` ascending. Row fields: `id`, `name`, `folderName` (the newest folder or null), `videoCount`,
  `documentCount`, `missing` (the `PlayerDetail` names in `MissingDetails()` order), `waitingSince` (= `CreatedAt`).
  `missing` is computed in SQL from the null columns.
- `GET /admin/waiting/summary` → `{waiting, noVolleybox, noPhoto, publishedToday}` (published today = `PublishedAt` on
  today's Tehran date).
- `PATCH /admin/players/{id}` with `UpdatePlayerDto`. Send `UpdatePlayerDetailsCommand` and answer `AdminPlayerDto`.
- `PUT /admin/players/{id}/photo`: reuse the Mini App photo command and pipeline (`MiniAppPhotoEndpoints`), with the
  same limits and content types. There is no test; copy `MiniAppPlayerPhotoTests` for one if you like.
- `POST /admin/players/{id}/publish` → 200 `AdminPlayerDto`.
  - It calls `Player.Publish(now)`; an incomplete player is the domain's 422 `player.incomplete`.
  - It is idempotent: a second call answers 200 and writes no second audit entry.
  - Audit `player.published`, target `Player`.
  - The analyst's bot message already exists (`PlayerPublishedNotice` on `PlayerPublished`), as does ingest
    (`IngestPublishedPlayer`). Check that both run.
- Web `web/src/lib/admin/waiting.ts` (`volleyboxId`, `missingDetails`). The component is
  `web/src/components/admin/waiting/complete-details.tsx` (props as in the test; `onSave` gets only the editable
  fields). The page `web/src/app/admin/(console)/waiting/page.tsx` follows the canvas `Waiting` exactly: the stat tiles,
  the filter chips (`?missing=`), the table, and the drawer for the selected row (`?player=`). Publish calls save, then
  publish, through `/api/admin/...`. "Next waiting player" selects the next row.

---

## Phase 6: new share link modal (A-6)

Test: `new-link-modal.test.tsx`.

- `web/src/components/admin/links/new-link-modal.tsx`: `NewLinkModal({ player: {id,name}, defaultExpiryDays, onCreate, onClose })`.
  - Fields: recipient email (required), recipient name, organisation, the expiry chips 7/30/90, and a personal note
    (≤ 500).
  - `onCreate` gets the `POST /admin/links` body shown in the test. The page wires it to `/api/admin/links` and maps
    `{code}` to the URL `https://{host}/r/{code}`.
  - Dialog behaviour as the document viewer. Show the canvas notice text.
- No backend change: `POST /admin/links` exists (L-1). `defaultExpiryDays` comes from `GET /admin/settings`.
- **(proposed)** Picking single files instead of "all visible" (decisions §7, "waits for A-6") stays a follow-up: it
  needs a domain change to `ShareLink`. Put it in the PR as a question for the owner and add a backlog row if they
  want it.
- Also build the Share links page (`/admin/links`, canvas `AdminLinks`) on the existing `GET /admin/links` with the
  status chips and the Pause/Resume/Revoke buttons. "+ New link from a player" goes to `/admin/players`.

---

## Phase 7: viewers (A-7, A-11)

Tests: `ViewerTests`, `AdminViewersTests`, `replay-heatmap.test.tsx`.

### 7.1 Viewer identity

- `Domain/Viewers/Viewer.cs` and table `viewers` (`id`, `email` unique, `first_seen_at`).
- Register a viewer for each recipient email when a link is created (`CreateShareLinkCommandHandler`), and when the
  gate starts a session for an email that has no row (forwards). Use "insert if absent" (`ON CONFLICT DO NOTHING` via a
  small `IViewerRegistry` port), so concurrent requests never fail.
- The migration back-fills `viewers` from the existing recipients' emails and `viewer_sessions.email`.
- Admin URLs carry the viewer **id**, never the email.

### 7.2 Endpoints (one `IViewerReader`, SQL projections, all paged)

- `GET /admin/viewers?q&activity=all|active|never&playerId&page&pageSize`. Row fields: `id`, `email`,
  `organisation` (the newest recipient's), `players` (≤ 3 names), `playerCount`, `plays`, `watchSeconds`,
  `lastSeenAt`, `accessUntil` (the latest `ExpiresAt` of the open links), `account`.
  - `account`: `SignedIn` when a `ViewerAccount` exists, else `LinkOnly` when they have a viewer session, else `NotYet`.
  - `q` matches the email or the organisation with `ILIKE`.
  - `playerId` keeps the viewers of links to that player.
  - Order: `lastSeenAt` descending with nulls last, then email.
- `GET /admin/viewers/{id}` → the profile: `id`, `email`, `organisation`, `firstSeenAt`, `lastSeenAt`, `account`,
  `plays`, `watchSeconds`, `playersAccessible`, `averageCompletion`. 404 when unknown.
- `GET /admin/viewers/{id}/access?page&pageSize`: one row per (link, player) of links that list the email among their
  recipients' emails, or that the viewer opened. Fields: `playerId`, `playerName`, `linkId`, `code`, `status`
  (the link's access now), `grantedAt` (link `CreatedAt`), `expiresAt`, `plays`, `watchSeconds`.
- `GET /admin/viewers/{id}/videos?playerId&page&pageSize`: one row per video, with the L-6 metrics as
  `GetLinkViewingQuery` computes them, across all links. Fields: `playerId`, `playerName`, `assetId`, `title`,
  `plays`, `watchSeconds`, `uniqueSeconds`, `completion`, `lastWatchedAt`.
- `GET /admin/viewers/{id}/videos/{assetId}/heatmap`: `assetId`, `title`, `durationSeconds`, `bucketSeconds`,
  `buckets`.
  - `bucketSeconds = max(5, ceil(duration / 200))` **(proposed)**.
  - `buckets = WatchMetrics.Hotspots(all this viewer's ranges of the video, duration, bucketSeconds)`.
  - 404 when the viewer never played it.
  - The ranges of one viewer and one video are small. Load only those rows.
- `GET /admin/viewers/{id}/sessions?cursor&pageSize`: the viewer's watches, newest `StartedAt` first. Fields:
  `watchId`, `playerName`, `videoTitle`, `startedAt`, `lastSeenAt`, `watchSeconds`, `completion`.
  Device and location are **not** collected (privacy). The canvas column stays out.
- **Out of scope (proposed follow-ups):** "Block viewer", the viewers CSV export, and per-access Revoke. Revoke works
  per link on the links page.

### 7.3 Web

- `web/src/components/admin/viewer/replay-heatmap.tsx` as the test says. The time label is `m:ss–m:ss`.
- Pages `viewers/page.tsx` (canvas `AdminViewers`: search and activity chips in the URL) and `viewers/[id]/page.tsx`
  (canvas `AdminViewer`: the KPI tiles, Access, the watch log with player chips, a heatmap for the selected row via
  `?video=`, and Sessions).

---

## Phase 8: users & roles + audit (A-8)

Test: `AdminUsersTests`.

- `GET /admin/users?status=active|pending|declined|removed&page&pageSize`. Row fields: `id`, `displayName`,
  `username`, `email` (masked), `signIn` (`Email` for admins with an email, else `Telegram`), `role`, `status`,
  `playerCount` (players whose `AnalystId` is the user), `lastActiveAt`, `requestedAt`.
- `POST /admin/users/{id}/approve` with `{ role }` and `POST /admin/users/{id}/decline`. Reuse the bot's commands from
  `AccessCommands.cs`, same audit actions. They answer `AdminUserDto`.
- `PATCH /admin/users/{id}` with `{ email?, role? }`. The email is applied first, then the role.
  - `user.admin_needs_email` comes from the domain.
  - Demoting the last active admin is `DomainException("user.last_admin")` → 422. Count the active admins in the
    handler.
  - Audit `user.role_changed` and `user.email_changed` (the data holds no email).
- `POST /admin/users/{id}/remove` with `{ reassignTo? }`.
  - The last active admin → 422 `user.last_admin`.
  - A user with players and no `reassignTo` → 400 keyed `reassignTo`.
  - `reassignTo` must be an active admin (else 400).
  - The players move with `Player.AssignAnalyst`, in one transaction. Audit `user.removed`.
- `GET /admin/audit?action&cursor&pageSize`: newest first. Fields: `id`, `at`, `actorId`, `actorName` (null for the
  system), `action`, `targetType`, `targetId`, `data`.
- **Out of scope (proposed follow-up):** "Invite admin by email". `User` needs a Telegram id today; inviting someone
  without Telegram needs a domain change. The page shows "Promote an analyst" instead (add their email + Admin).
- Web `users/page.tsx` follows the canvas `AdminUsers`: pending banner(s) with Approve as Analyst and Decline, the team
  table with role and email edit in a small dialog, the static permissions table from `copy.ts`, and the audit log with
  "Load more" (cursor).

---

## Phase 9: jobs & email queue (A-9)

Tests: `OutgoingEmailRetryTests`, `AdminJobsTests`.

- `OutgoingEmail.Retry(now)`: only from `Failed`, else `DomainException("email.not_failed")`. It sets `Queued`,
  `Attempts = 0`, keeps `LastError`, and raises `EmailQueued(Id)` (the existing sender picks it up).
- `GET /admin/jobs?state=queued|copying|processing|failed&page&pageSize` → assets not `Ready`, newest `CreatedAt`
  first. Fields: `assetId`, `playerId`, `playerName`, `title`, `fileName`, `kind`, `state`, `failureReason`,
  `createdAt`. `ready` and unknown states → 400.
- `GET /admin/jobs/summary` → `{queued, copying, processing, failed, folders, lostFolders, lastDriveSyncAt,
  emailsSentLast24Hours, emailsFailed, emailsQueued}`.
- `POST /admin/emails/{id}/retry` → 202, audit `email.retried`, 404 when unknown, 422 `email.not_failed`.
- `POST /admin/files/{id}/retry` is Phase 4's.
- Web `jobs/page.tsx` follows the canvas `AdminQueues` with the tabs Jobs and "Email queue" (`?tab=`).
  - Jobs: the summary tiles and the jobs table with Retry.
  - Email queue: the existing `GET /admin/emails` with Retry on failed rows.
  - The provider health tiles on the canvas are **(proposed) out of scope**: we keep no provider health. Show "Sent ·
    24 h", Failed and Queued instead.

---

## Phase 10: settings page (A-10 web)

Test: `web/src/lib/admin/settings-form.test.ts`.

- `web/src/lib/admin/settings-form.ts` (`validateSettings`, `LINK_EXPIRY_CHOICES`).
- `settings/page.tsx` follows the canvas `AdminSettings`.
  - Editable: the contact email, the default expiry chips (7/30/90, no "Never"), the digest time and the quiet hours
    (`<input type="time">`). Save goes through `PUT /api/admin/settings`.
  - Read-only rows: sessions (admins 30 days, viewers 7 days, sign-in link 15 min single use), email providers and
    media protection.
  - The canvas's link toggles (verify on new devices, allow forwarding, notify on open) are per-link switches today.
    Leave them out of Settings **(proposed)**.

---

## Finish

1. Run the whole suite: `dotnet test` (solution), `cd web && npx vitest run && npm run typecheck && npm run lint`,
   `cd edge && npx vitest run`. Then `dotnet format --verify-no-changes`. Report the real numbers.
2. Playwright visual pass: add `web/e2e/admin.visual.spec.ts` with screenshots of each console page on stubbed data,
   as the other `*.visual.spec.ts` do. Compare by eye against the canvas boards.
3. Housekeeping (CLAUDE.md "Keep up to date"):
   - README: roadmap rows A-2…A-11 done, a structure note for `web/src/app/admin`.
   - `decisions.md`: record the readers added as §2 exceptions, the proposed values the owner confirms, and the
     follow-ups.
   - `backlog.md`: mark A-2…A-11 done and add rows for the follow-ups (Activity tab, Block viewer, viewers CSV, invite
     admin by email, single-file links, provider health) if the owner wants them.
   - `.gitignore`/`.dockerignore` if anything new.
   - Scalar docs for every endpoint (summary, description, examples, every error answer, auth).
4. Commit per phase with clear English messages. Open one PR into `main` that lists every **(proposed)** value and the
   follow-ups. CI must pass.
5. Remind the owner to run **k6** for the console APIs (K-5): offer to extend `perf/k6/` with the dashboard, players
   list, viewers list and heatmap.
