# Plan: finish Epic 6 — access-request inbox (P-4), web sign-in (W-1) and the home-page sign-in (P-7)

Written 2026-11-14 (Opus) for an implementing model (Sonnet). Branch `feature/access-inbox-sign-in`, cut from `main`
at `b79da4f`. **The tests are already written and committed red.** Your job: make every test green without weakening
one, following the phases below in order. These three tasks are the last open ones in **Epic 6** (SSD-7):
P-4 SSD-67, P-7 SSD-99, W-1 SSD-133.

**Why one branch:** P-4 is independent of the other two. P-7 depends on W-1 (its Sign in button and one-click request
already exist from PR #60/#63; what P-7 still lacks is a way to *become* signed in, which is W-1). So W-1 closes P-7,
and P-4 can be built in the same branch without touching their files.

Owner decisions behind this plan are in `.claude/decisions.md`: §7 (access requests, forwarding alerts), §7a (accounts
and login, "Access requests"), §8a (magic-link sign-in A-1/P-5), the P-8 paragraphs in §11, and §1b (security rules).
Read those first.

---

## 0. Before you start

- Read `.claude/CLAUDE.md`. Everything there applies:
  - MediatR for every command and query; a FluentValidation validator for each one with input.
  - Specification pattern for query conditions, `AsNoTracking` projections on reads, lists paged in SQL.
  - Mapperly for DTOs; entities never leave the Application layer.
  - XML doc comments on **every** member (public and private, tests too). TSDoc on every exported TS function and
    component.
  - English only. UI copy lives in `copy.ts` files (web) and `BotTexts.resx` (bot).
  - Source-generated Serilog `[LoggerMessage]`; never log an email, token or code.
  - Scalar docs complete for every new endpoint (name, summary, description, tag, examples, every error response,
    auth requirement).
- **Never edit a test to make it pass.** If a test truly contradicts `decisions.md`, stop and ask the owner. Tests
  changed on purpose by Opus (already done): `home-page.test.tsx` and `request-access-modal.test.tsx` now expect the
  Sign in link `/sign-in` (the address the API's sign-in emails already use, `WebLinks.ViewerSignIn`), and
  `BackendTestHost` exposes the bot recorder as `Bot`.
- Values marked **(proposed)** are Opus's proposals. They are what the tests expect. List them in the PR description
  so the owner can confirm them, and record them in `.claude/decisions.md` the same day, marked "proposed, confirm".
- Jira (`bonyadin2000.atlassian.net`, project SSD): move SSD-67, SSD-133 to *In Progress* when you start (SSD-99 is
  already there). Comment and close each one when its phase is green and committed.

### The red tests

| Test file | Covers |
|---|---|
| `tests/SsSport.Drive.Domain.UnitTests/AccessRequests/AccessRequestTests.cs` (5 new tests at the end) | `Approve` / `Decline` on the aggregate |
| `tests/SsSport.Drive.Application.UnitTests/Public/AccessRequestCallbacksTests.cs` | bot buttons `areq:ok:` / `areq:no:`, approval email text |
| `tests/SsSport.Drive.Api.IntegrationTests/Public/AccessRequestInboxTests.cs` | bot alert, `/admin/access-requests` list/approve/decline, bot presses |
| `web/src/lib/auth/sign-in-server.test.ts` | `/api/auth/{audience}/link`, `/api/auth/{audience}/session`, `/api/admin/sign-out`, `readAdmin` |
| `web/src/components/viewer/sign-in/sign-in-panel.test.tsx` | the `/sign-in` and `/admin/sign-in` page body |
| `web/src/components/viewer/sign-in/sign-in-client.test.ts` | the browser client of the panel |
| `web/src/components/admin/admin-home.test.tsx` | the `/admin` placeholder after sign-in |
| `web/src/components/viewer/public/home-page.test.tsx`, `request-access-modal.test.tsx` | Sign in → `/sign-in` |

Commands:

```bash
dotnet test tests/SsSport.Drive.Domain.UnitTests
dotnet test tests/SsSport.Drive.Application.UnitTests
dotnet test tests/SsSport.Drive.Api.IntegrationTests --filter "FullyQualifiedName~AccessRequestInbox"
dotnet test tests/SsSport.Drive.ArchitectureTests
cd web && npx vitest run && npm run lint && npm run typecheck
```

Integration tests need Docker (Testcontainers). Before each commit run the **full** suites, not only the filters.

### Closest models to copy

| What | Copy from |
|---|---|
| First decision is final, decider recorded, audited, email on the good outcome | `src/SsSport.Drive.Application/Links/LinkForwards.cs` (`DecideForwardCommand`) and `src/SsSport.Drive.Domain/Links/LinkForward.cs` (`Decide`) |
| Bot alert to every active admin with two buttons | `src/SsSport.Drive.Application/Telegram/Links/ForwardAlerts.cs` (`ForwardCallbacks`, `NotifyAdminsOfForward`) |
| Bot button routing | `HandleTelegramUpdate.cs` `OnForwardButtonAsync` |
| Creating a link (recipient, unused code, audit) | `CreateShareLinkCommandHandler` in `src/SsSport.Drive.Application/Links/ShareLinks.cs` |
| Email composer + template + content | `ForwardKeptComposer`, `EmailContent.ForwardKept`, `EmailTemplates` |
| Admin endpoints, `.RequireAdmin()`, Scalar docs, paged list | `src/SsSport.Drive.Api/Links/ShareLinkEndpoints.cs`, `GetShareLinksQuery`, `IShareLinkListReader` |
| Web same-origin forwarding route, cookie handling | `web/src/lib/viewer/public-server.ts` (`forwardPublic`, `signOut`) and `gate-server.ts` helpers (`isSameOrigin`, `problem`, `readCookie`, `clientAddress`) |
| Dialog/form UI and copy | `web/src/components/viewer/public/request-access-modal.tsx`, `copy.ts`; the gate's styles under `components/viewer/gate` |

---

## Phase 1 — P-4 domain (SSD-67)

`src/SsSport.Drive.Domain/AccessRequests/AccessRequest.cs`:

- Add `Guid? DecidedByUserId`, `DateTimeOffset? DecidedAt`, `Guid? ShareLinkId` (private setters).
- `bool Approve(Guid adminId, Guid shareLinkId, DateTimeOffset now)`: `ArgumentException` for an empty id; returns
  `false` and changes nothing if not `Pending`; otherwise sets status, decider, time and link, returns `true`.
- `bool Decline(Guid adminId, DateTimeOffset now)`: same rules, no link.
- Update the enum's doc comment ("P-4 adds the decisions" is now done).
- EF: map the three new columns in `PublicAccessConfiguration.cs` (the `AccessRequest` mapping), add an index on
  `(status, created_at)` for the inbox, and add a migration (`dotnet ef migrations add AddAccessRequestDecisions`
  from the Infrastructure project, as the earlier migrations were made).

Green: `AccessRequestTests`.

## Phase 2 — P-4 application, bot and API (SSD-67)

### 2a. Approval email
- `EmailTemplates.AccessApproved = "access.approved"`.
- `EmailContent.AccessApproved(EmailAddress to, string playerNames, Uri link)`: subject
  **"Your SS-SPORT Drive access is approved"**; text and HTML name the players and carry the link; every value in the
  HTML is `WebUtility.HtmlEncode`d (the test checks a name with `<b>`). Same shape and sign-off as `ForwardKept`.
- `AccessApprovedComposer` (template `access.approved`, data `{"shareLinkId": "…"}`): loads the link and its players
  (`PlayersByIdsSpec`), names them in link order, builds `webLinks.ViewerLink(code)`. Register it next to
  `ForwardKeptComposer` in `Application/DependencyInjection.cs`.
- Queue it as `EmailKind.General` (it goes through Google SMTP, not the code providers; the test checks this).

### 2b. One decision service, two entry points
Create `src/SsSport.Drive.Application/Public/AccessRequestInbox.cs` (namespace `SsSport.Drive.Application.Public`):

- An internal/shared `AccessRequestDecider` (a scoped service, or a static helper taking the repositories) that both
  the API commands and the bot command call, so the rules live once:
  - load the request (`NotFoundException("AccessRequest")` if missing);
  - already decided → throw a new **`ConflictException(code, message)`** with code
    **`access_request.already_decided`** (add it to `Application/Exceptions/ApplicationExceptions.cs` and map it to
    409 with the `code` extension in `ApplicationExceptionHandler`, like the other coded problems);
  - **approve:** create the link exactly as `CreateShareLinkCommandHandler` does (extract its recipient/code/link
    creation into a reusable piece rather than copying it — DRY), with:
    - recipient: a **new** `Recipient` with `Emails = [request.Email]`, `Name` = the email's local part
      (cut to `Recipient.MaxNameLength`), no organisation, no country **(proposed)**;
    - players: `PlayerIds` from the admin, or `[request.PlayerId]` when none are given;
    - expiry: `ExpiresInDays` from the admin (validated 7/30/90 with `ShareLinkRules.IsExpiryChoice`), or
      **30 days** by default **(proposed)**;
    - note: from the admin, or none; settings: `ShareLinkSettings.Default`;
    - then `request.Approve(admin, link.Id, now)`, queue the approval email, audit
      **`access_request.approved`** (target `AccessRequest`, data `{"shareLinkId": "…"}`) — and the normal
      `share_link.created` audit entry from the link creation;
  - **decline:** `request.Decline(admin, now)`, audit **`access_request.declined`**, **no email** **(proposed;
    same as a blocked forward, so the requester learns nothing; confirm with the owner)**.
  - All in one unit-of-work save (the pipeline saves after the handler).
- API commands (both `IAuthorizedRequest` with `ShareLinkRules.AdminRoles`, actor from `ICurrentUser`):
  - `ApproveAccessRequestCommand(Guid Id, int? ExpiresInDays, IReadOnlyList<Guid>? PlayerIds, string? Note)` →
    `AccessRequestDto`; validator: `ValidId`, expiry null or 7/30/90 (400 otherwise — the test sends 45), at most
    `ShareLink.MaxPlayers` ids, note ≤ `ShareLink.MaxNoteLength`.
  - `DeclineAccessRequestCommand(Guid Id)` → `AccessRequestDto`; validator: `ValidId`.
- Bot command `DecideAccessRequestFromBotCommand(long AdminTelegramId, Guid RequestId, bool Approve)` →
  `AccessRequestDecisionResult(Outcome, Status, Email, LinkCode, DecidedBy)` mirroring `ForwardDecisionResult`:
  `NotAllowed` when the presser is not an active admin (nothing changes), `NotFound`, `Decided`, `AlreadyDecided`.
  Bot approval always uses the defaults (30 days, the requested player, no note). Validator like
  `DecideForwardCommandValidator`.
- Query `GetAccessRequestsQuery(PageRequest Paging, string? Status)` : `IPagedQuery<AccessRequestListItemDto>`,
  admins only. `Status` is `Pending` (default when missing), `Approved` or `Declined`, case-insensitive; anything else
  is a validation error (400). Newest first (`created_at DESC`, then id). Paged **in SQL**: add an
  `IAccessRequestListReader` in `Abstractions/Persistence` with an Infrastructure implementation that joins
  `access_requests` → `players` (and `users` for the decider's display name, `share_links` for the code) in one
  `AsNoTracking` projection, the way `ShareLinkListReader` does. The status condition goes in a specification
  (`AccessRequestsByStatusSpec`) or, if you need the join in raw projection, record it as an exception like
  `IPublicPlayerReader` does.

DTO shape (JSON, camelCase — the integration tests read these names):

```json
{
  "id": "019a…",
  "player": { "id": "…", "name": "Arash Tehrani", "slug": "arash-tehrani" },
  "email": "j.demir@gmail.com",
  "about": "Aurora Volley, head scout…",
  "source": "EmailCode",
  "status": "Pending",
  "createdAt": "2026-11-14T09:00:00+00:00",
  "decidedAt": null,
  "decidedBy": null,
  "shareLink": null
}
```

`shareLink`, when approved: `{ "id", "code", "path": "/r/{code}", "expiresAt" }`. The list item and the decision
answer use the same DTO. Map with Mapperly.

### 2c. Bot alert and buttons
- New folder `src/SsSport.Drive.Application/Telegram/AccessRequests/` (namespace
  `SsSport.Drive.Application.Telegram.AccessRequests`):
  - `AccessRequestCallbacks` like `ForwardCallbacks`: `Keyboard(Guid)` → one row, **"Approve (30 days)"** then
    **"Decline"** (texts in `BotTexts.resx`), data `areq:ok:{id:N}` / `areq:no:{id:N}`; `TryParse(string, out bool
    approve, out Guid id)`; `Describe(AccessRequestDecisionResult)` → the reply text. The approved reply must contain
    the new **link code** (the test waits for it); declined, already-decided, gone and not-allowed texts like the
    forward ones.
  - `NotifyAdminsOfAccessRequest : IBackgroundEventHandler<AccessRequested>`: skips a request that is no longer
    pending; sends every active admin (`ActiveAdminsSpec`) one message with the player's name, the full email and the
    description (BotTexts `AccessRequestAlert`), with the keyboard. Immediate, not held by the notice rules (decisions
    §7, like forward alerts). The email appears only in this bot message, never in logs.
- `HandleTelegramUpdate`: before the team-access review buttons, route `AccessRequestCallbacks.TryParse` to an
  `OnAccessRequestButtonAsync` that mirrors `OnForwardButtonAsync` (non-admin → answer with an alert and log a
  structured warning without the email).
- Register the background handler where `NotifyAdminsOfForward` is registered.

### 2d. API endpoints
New `src/SsSport.Drive.Api/Public/AccessRequestEndpoints.cs` (tag **"Access requests"**), mapped next to the share
link endpoints, every one with `.RequireAdmin()`:

| Method | Path | Body | Answer |
|---|---|---|---|
| GET | `/admin/access-requests` | `?status=&page=&pageSize=` | 200 paged list |
| POST | `/admin/access-requests/{id}/approve` | `{ expiresInDays?, playerIds?, note? }` | 200 DTO; 400; 401; 403; 404; 409 |
| POST | `/admin/access-requests/{id}/decline` | `{}` | 200 DTO; 401; 403; 404; 409 |

POSTs are JSON only (the API's CSRF rule). Scalar: name, summary, description, examples (pending item, approved
answer, 409 problem), all error responses, and "Admin: Mini App signature or `X-Admin-Session`". The architecture
test that enumerates `/admin/*` endpoints must stay green.

Green: `AccessRequestCallbacksTests`, `AccessRequestInboxTests`, the rest of the API and architecture tests.

### 2e. Out of scope for P-4 (recorded as new work)
The **console web page** for the inbox needs the admin console shell (Epic 7), which does not exist yet. P-4 ships the
API the page will use, plus the bot. Opus added backlog task **A-12** (Jira SSD-137) "Access-requests console page"
for the page. Do not build it here.

## Phase 3 — W-1 web sign-in (SSD-133), which closes P-7 (SSD-99)

### 3a. Server helpers — `web/src/lib/auth/sign-in-server.ts`
Exports (names are what the tests import):

- `type Audience = "viewer" | "admin"`, `isAudience(value)`.
- `ADMIN_COOKIE = "__Host-ssd_as"`. The viewer cookie stays `ACCOUNT_COOKIE = "__Host-ssd_acct"` from
  `public-server.ts` (decisions §11 P-8 wins over the backlog's `__Host-ssd_va`); import it, do not redefine it.
- `VIEWER_SESSION_SECONDS = 604800` (7 days), `ADMIN_SESSION_SECONDS = 2592000` (30 days) — decisions §7a.
- `requestSignInLink(request, apiBaseUrl, audience, fetchImpl = fetch)`: 403 if not same-origin, 415 if not JSON;
  forwards the body to `POST {api}/auth/{audience}/link` with `Content-Type: application/json` and
  `X-Forwarded-For`; passes status, `Content-Type` and `Retry-After` through; 502 when the API cannot be reached.
- `startSession(request, apiBaseUrl, audience, fetchImpl = fetch)`: the Continue button's **form post**
  (`application/x-www-form-urlencoded`, field `token`):
  - not same-origin → 403, nothing sent (login CSRF);
  - no token → 303 to the error page, nothing sent;
  - `POST {api}/auth/{audience}/session` with JSON `{ token }`;
  - 200 → 303 to `/` (viewer) or `/admin` (admin) with
    `Set-Cookie: <cookie>=<sessionToken>; Path=/; Max-Age=<seconds>; HttpOnly; Secure; SameSite=Lax` for the viewer
    and `SameSite=Strict` for the admin **(proposed)**; never a `Domain`;
  - API answered non-2xx → 303 to `/sign-in?error=expired` or `/admin/sign-in?error=expired`, no cookie;
  - API unreachable → same pages with `?error=unavailable`.
- `signOutAdmin(request, apiBaseUrl, fetchImpl = fetch)`: like `signOut` in `public-server.ts`, but the admin cookie,
  header `X-Admin-Session`, API `/auth/admin/sign-out` (JSON `{}`), and 303 to `/admin/sign-in`.
- `readAdmin(apiBaseUrl, token, fetchImpl = fetch)` → `{ email, displayName } | null` from `GET /auth/admin/me`;
  null without a token, on any non-2xx, or when the API is down.

Reuse `isSameOrigin`, `problem`, `readCookie`, `clientAddress` from `gate-server.ts`; if two modules end up with the
same `apiUrl`/cookie-string helpers, move them to one shared place (DRY).

### 3b. Routes
- `web/src/app/api/auth/[audience]/link/route.ts` (POST) and `.../session/route.ts` (POST): 404 for an unknown
  audience (`isAudience`), otherwise call the helpers with `apiBaseUrl()`.
- `web/src/app/api/admin/sign-out/route.ts` (POST).

### 3c. Pages and components
- `web/src/components/viewer/sign-in/sign-in-client.ts`: `type SignInClient = { requestLink(email): Promise<…> }`
  and `createSignInClient(audience, fetchImpl = fetch)`: POST JSON to `/api/auth/{audience}/link`; 202 → `{ kind:
  "sent" }`; 429 → `{ kind: "too-many", retryAfterSeconds }` from `Retry-After`; 400/422 → `{ kind: "invalid" }`;
  anything else or a network error → `{ kind: "failed" }`.
- `web/src/components/viewer/sign-in/sign-in-panel.tsx` (`"use client"`), props
  `{ audience, token?, error?, client }`; copy in `web/src/components/viewer/sign-in/copy.ts`. The test strings are
  the copy:
  - heading "Sign in" (viewer) / "Admin sign-in" (admin);
  - email box labelled "Email" (`type="email"`, `autocomplete="email"`), button "Send sign-in link";
  - a malformed email → "Enter a valid email." and no call; the email is trimmed before sending;
  - sent → heading "Check your email", a line naming the address, and "…works once, for 15 minutes…";
  - too many → "Too many sign-in links. Try again in a minute."; failed/invalid → "Something went wrong. Please try
    again." (invalid may reuse the field message if you prefer, but the failed text must match);
  - `error="expired"` → `role="alert"` "That sign-in link has expired or was already used. Ask for a new one.";
    `error="unavailable"` → "Sign-in is unavailable right now. Please try again shortly."; any other value → no alert
    (never render the raw query value);
  - with a `token`: **only** a `<form method="post" action="/api/auth/{audience}/session">` holding a hidden `token`
    input and a submit button "Continue". It must not submit itself: mail scanners open links, and the token works
    once.
  Style it with the viewer shell and the gate's look (there is no canvas artboard for sign-in; keep it consistent
  with the Gate).
- Pages:
  - `web/src/app/(viewer)/sign-in/page.tsx`: reads `?token=` and `?error=` (first value only), renders the panel
    with `createSignInClient("viewer")`. A signed-in viewer (cookie present and `/auth/viewer/me` OK) is redirected
    home.
  - `web/src/app/admin/sign-in/page.tsx`: the same for admins (outside the `(viewer)` group, no Mini App styles).
  - `web/src/app/admin/page.tsx`: `readAdmin` with the admin cookie; nobody → redirect to `/admin/sign-in`;
    otherwise `<AdminHome admin={…} />`.
  - `web/src/components/admin/admin-home.tsx`: heading "SS-SPORT Drive console", "Signed in as {displayName}
    ({email})", and a POST form to `/api/admin/sign-out` with a submit button "Sign out". Placeholder until Epic 7.
  - Add `robots: { index: false }` metadata to all three pages; read cookies/search params inside `<Suspense>` like
    `(viewer)/page.tsx`.
- `web/src/components/viewer/public/copy.ts`: `signInHref: "/sign-in"` (this turns the two P-7 tests green).
- Check `web/src/lib/security-headers.ts` covers the new pages (site CSP, not the Mini App one) and that
  `/admin/*` pages send `Cache-Control: no-store`.

Green: all web tests, `npm run lint`, `npm run typecheck`.

## Phase 4 — Housekeeping, commit, PR

- `.claude/decisions.md`: a paragraph "Access-request inbox and web sign-in (P-4, W-1, P-7; 2026-11-14)" with the
  (proposed) values: 30-day default, recipient name from the email, decline sends nothing, bot Approve uses
  defaults, cookie names and SameSite, Continue button instead of auto-redeem, console page moved to A-12.
- `.claude/backlog.md`: mark P-4, P-7, W-1 done when merged; A-12 is already added.
- `README.md` roadmap: Epic 6 complete; Scalar tag list if the README lists tags.
- `.gitignore` / `.dockerignore`: nothing new expected; check.
- Three commits are fine (one per phase), English messages, then one PR into `main` titled
  "Finish Epic 6: access-request inbox, web sign-in" with the (proposed) values listed. CI must pass.
- Jira: comment on SSD-67, SSD-133, SSD-99 with branch, commit, test results; move to *Done*.
- Remind the owner to run a **k6** benchmark for `/admin/access-requests` and the sign-in routes, and offer to write
  the script (CLAUDE.md step 9).
