# Backend consolidation plan: email queue, magic-link sign-in, watch tracking

Written 2026-11-12 (Opus) for an implementing model (Sonnet). Branch: `feature/backend-tracking-email-signin`, cut from
`main` at `68780ef` (includes L-3 / PR #59). **The tests are already written and committed red.** Your job: make every
test green without weakening them, following the steps below in order.

## 0. Before you start

- Read `.claude/CLAUDE.md` (code rules, security rules, Scalar rules). Everything there applies: MediatR for every
  command/query, Specification pattern for query conditions, `AsNoTracking` projections on reads, Mapperly for DTOs,
  XML doc comments on **every** member (public and private, tests too), English only, Serilog source-generated
  `[LoggerMessage]` logging with no emails / tokens / codes, FluentValidation in the pipeline, lists paged in SQL.
- Copy the existing style. The closest models:
  - Domain aggregate with time-ordered id and events: `src/SsSport.Drive.Domain/Links/LinkForward.cs`, `ViewerAccess.cs`.
  - Background event handler run by Hangfire: `SendEmailCode` in `src/SsSport.Drive.Application/Links/ViewerGate.cs`.
  - Hangfire job + queue + server: `src/SsSport.Drive.Infrastructure/BackgroundJobs/BotNotices.cs`.
  - Public viewer endpoints with `X-Viewer-Session`: `src/SsSport.Drive.Api/Links/ViewerContentEndpoints.cs` and
    `src/SsSport.Drive.Application/Links/ViewerContent.cs` (order of checks, error codes).
  - Admin endpoints + `IAuthorizedRequest` + `ShareLinkRules.AdminRoles`: `ShareLinkEndpoints.cs`, `ShareLinks.cs`.
  - Cursor and page paging: `IPagedReader` (`PageAsync`, `CursorPageAsync`), `src/SsSport.Drive.Application/Paging/*`.
  - EF configuration: `src/SsSport.Drive.Infrastructure/Persistence/Configurations/ViewerGateConfiguration.cs`.
- Never edit a test to make it pass. If a test is truly wrong (contradicts decisions.md), stop and ask the owner.
- Values marked **(proposed)** below are Opus's proposals; they are what the tests expect. List them for the owner in
  the PR description (section 6) so the owner can confirm.

### Test files (all red now: they reference types that do not exist yet)

| Phase | Test file |
|---|---|
| 1 | `tests/SsSport.Drive.Domain.UnitTests/Emails/OutgoingEmailTests.cs` |
| 1 | `tests/SsSport.Drive.Infrastructure.IntegrationTests/Email/EmailTransportTests.cs` (replaces the deleted `EmailCodeSenderTests.cs`) |
| 1 | `tests/SsSport.Drive.Infrastructure.IntegrationTests/Email/EmailDispatcherTests.cs` |
| 1 | `tests/SsSport.Drive.Application.UnitTests/Emails/SendQueuedEmailTests.cs` |
| 1 | `tests/SsSport.Drive.Application.UnitTests/Links/ForwardKeptEmailTests.cs` (L-3b) |
| 1 | `tests/SsSport.Drive.Api.IntegrationTests/Emails/EmailQueueEndpointTests.cs` |
| 1 | `tests/SsSport.Drive.Api.IntegrationTests/Links/ViewerGateEndpointTests.cs` (only its email stand-in changed: it is now an `IEmailTransport` named `Resend`) |
| all | `tests/SsSport.Drive.Api.IntegrationTests/Support/BackendTestHost.cs` (shared host, not a test) |
| 2 | `tests/SsSport.Drive.Domain.UnitTests/Accounts/SignInTests.cs` |
| 2 | `tests/SsSport.Drive.Api.IntegrationTests/Accounts/SignInEndpointTests.cs` |
| 3 | `tests/SsSport.Drive.Domain.UnitTests/Tracking/WatchMetricsTests.cs` |
| 3 | `tests/SsSport.Drive.Domain.UnitTests/Tracking/WatchSessionTests.cs` |
| 3 | `tests/SsSport.Drive.Api.IntegrationTests/Tracking/WatchEndpointTests.cs` |

Because C# test projects compile as a whole, nothing in a test project runs until **all** its referenced types exist.
Cheapest order: create every new type as a compiling stub first (step 0.1), then implement phase by phase and run
only that phase's tests with a filter.

### 0.1 Stubs first (one commit, no behaviour)

Create the public types and members listed in sections 1.1, 2.1 and 3.1 with `throw new NotImplementedException()`
bodies, so `dotnet build SsSport.Drive.slnx` is clean. Then fix any remaining compile errors the tests show (member
names, parameter order) by matching the tests exactly. Commit: "Add stubs for the email queue, sign-in and tracking".

Run a phase's tests with, for example:

```bash
dotnet test --project tests/SsSport.Drive.Domain.UnitTests -- --filter-namespace "SsSport.Drive.Domain.UnitTests.Emails"
dotnet test --project tests/SsSport.Drive.Api.IntegrationTests -- --filter-class "SsSport.Drive.Api.IntegrationTests.Emails.EmailQueueEndpointTests"
```

(Integration tests need Docker running: they use Testcontainers PostgreSQL.)

---

## Phase 1 — Email queue (L-4, SSD-60) + forward-kept email (L-3b, SSD-130)

Decisions §8: OTP codes via Resend, Brevo as fallback; all other email via Google SMTP (`info@ss-sport.co`); every email
through a Hangfire queue with retries; admins see the queue. Decisions §7 L-2: the code is drawn **inside** the job.

### 1.1 Domain — `src/SsSport.Drive.Domain/Emails/OutgoingEmail.cs`

```csharp
namespace SsSport.Drive.Domain.Emails;
public enum EmailKind { Code, General }                 // Code: Resend → Brevo; General: Google SMTP
public enum EmailStatus { Queued, Sent, Failed, Skipped }
public sealed record EmailQueued(Guid EmailId) : IDomainEvent;
public sealed class OutgoingEmail : AggregateRoot<Guid>
{
    public const int MaxAttempts = 4;       // first try + Hangfire's 3 retries
    public const int MaxDataLength = 2000;
    public const int MaxErrorLength = 300;
    public EmailKind Kind { get; }  public string Template { get; }  public EmailAddress To { get; }
    public string? Data { get; }    public EmailStatus Status { get; private set; }  public int Attempts { get; private set; }
    public string? Provider { get; private set; }  public string? LastError { get; private set; }
    public DateTimeOffset QueuedAt { get; }  public DateTimeOffset? LastAttemptAt { get; private set; }  public DateTimeOffset? SentAt { get; private set; }
    public static OutgoingEmail Queue(EmailKind kind, string template, EmailAddress to, string? data, DateTimeOffset now);
    public void RecordSent(string provider, DateTimeOffset now);
    public void RecordFailure(string error, DateTimeOffset now);
    public void Skip(DateTimeOffset now);
}
```

Rules (all in `OutgoingEmailTests`):
- Id `Guid.CreateVersion7(now)`. `Queue` raises `EmailQueued(id)`.
- Template must match `^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$` and be ≤ 64 chars, else `DomainException("email.template_invalid", …)`.
- Data: `null` allowed. Otherwise it must parse (System.Text.Json `JsonDocument`) as a JSON **object** and be
  ≤ `MaxDataLength` chars, else `email.data_invalid`. Containing `@` → `email.data_contains_email` (check this before
  the length/shape check order does not matter for the tests, but check `@` on valid JSON). Never put codes or tokens in data.
- `RecordSent`: `ArgumentException.ThrowIfNullOrWhiteSpace(provider)`; status must be `Queued` else
  `DomainException("email.not_queued")`; `Attempts++`, `Provider`, `SentAt = LastAttemptAt = now`, `Status = Sent`.
  Keeps `LastError` from earlier failures.
- `RecordFailure`: must be `Queued` (else `email.not_queued`); `Attempts++`; `LastError` = error cut to 300 chars;
  `LastAttemptAt = now`; `Status = Failed` when `Attempts >= MaxAttempts`, else stays `Queued`.
- `Skip`: must be `Queued` (else `email.not_queued`); `Status = Skipped`; `LastAttemptAt = now`; attempts unchanged.

### 1.2 Application ports — `src/SsSport.Drive.Application/Abstractions/Email/`

- Delete `IEmailCodeSender.cs`.
- `EmailMessage.cs`: `public sealed record EmailMessage(EmailAddress To, string Subject, string Text, string Html);`
- `IEmailTransport.cs`: `string Name { get; }`, `bool IsConfigured { get; }`, `Task SendAsync(EmailMessage message, CancellationToken cancellationToken);` (throws on failure).
- `IEmailDispatcher.cs`: `Task<string> SendAsync(EmailKind kind, EmailMessage message, CancellationToken cancellationToken);` returns the provider name.
- `IEmailComposer.cs`: `string Template { get; }`, `Task<EmailMessage?> ComposeAsync(OutgoingEmail email, CancellationToken cancellationToken);` (`null` = nothing to send → skip).
- `IWebLinks.cs` (new, `Abstractions/Web/` is fine too): builds absolute URLs on the web app from `Web:PublicBaseUrl`:
  `Uri ViewerLink(string code)` → `{base}/r/{code}`; `Uri AdminSignIn(string token)` → `{base}/admin/sign-in?token={token}`;
  `Uri ViewerSignIn(string token)` → `{base}/sign-in?token={token}`.

### 1.3 Application — `src/SsSport.Drive.Application/Emails/`

- `EmailTemplates.cs`: `public static class EmailTemplates { GateCode = "gate.code"; ForwardKept = "forward.kept"; AdminSignIn = "auth.admin_sign_in"; ViewerSignIn = "auth.viewer_sign_in"; }`
- Email copy (English) in a resource file `EmailTexts.resx` (+ generated designer, `PublicResXFileCodeGenerator` or
  a small static accessor). Move the existing code text from `EmailCodeMessage` (Infrastructure) here. HTML must encode
  any value with `System.Net.WebUtility.HtmlEncode` (codes and URLs). Subjects never contain codes or tokens.
- `SendQueuedEmail : IBackgroundEventHandler<EmailQueued>` with constructor
  `(IRepository<OutgoingEmail, Guid> emails, IUnitOfWork unitOfWork, IEnumerable<IEmailComposer> composers, IEmailDispatcher dispatcher, TimeProvider timeProvider, ILogger<SendQueuedEmail> logger)`:
  1. Load by id; `null` or `Status != Queued` → return (duplicate job).
  2. Find the composer whose `Template` equals the email's. None → `Skip(now)`, save, log **Error** (a bug), return.
  3. `ComposeAsync`; `null` → `Skip(now)`, save, return.
  4. `provider = await dispatcher.SendAsync(email.Kind, message, ct)`; `RecordSent(provider, now)`; save.
  5. On any exception except `OperationCanceledException`: `RecordFailure(exception.GetType().Name, now)` (type name
     only: messages may hold addresses), save, then rethrow if `Status == Queued` (Hangfire retries); if `Failed`, log
     Error with the email id and return without throwing.
- Composers (each a class implementing `IEmailComposer`, registered in `AddApplication`):
  - `GateCodeComposer` (`gate.code`): data `{"challengeId":"<guid>"}`. Loads the `EmailChallenge`; draws
    `secrets.NewCode()`; `challenge.IssueCode(secrets.HashCode(code), now)`; false or missing → return `null`;
    otherwise save (the hash must be stored before the mail leaves) and return the code email. This replaces
    `SendEmailCode`.
  - `ForwardKeptComposer` (`forward.kept`): data `{"shareLinkId":"<guid>"}`; loads the link; returns an email telling
    the viewer the link is open, with `webLinks.ViewerLink(link.Code.Value)`; a missing link → `null`.
  - `AdminSignInComposer` / `ViewerSignInComposer`: see Phase 2.
- Gate change in `ViewerGate.cs`: in `RequestEmailCodeCommandHandler`, after adding the `EmailChallenge`, add
  `OutgoingEmail.Queue(EmailKind.Code, EmailTemplates.GateCode, email, $$"""{"challengeId":"{{challenge.Id:D}}"}""", now)`
  to `IRepository<OutgoingEmail, Guid>`. Delete `SendEmailCode`. Keep `EmailCodeRequested` only if something else
  uses it; otherwise remove the event and its tests' references (grep first).
- L-3b in `LinkForwards.cs`: `DecideForwardCommandHandler` gets `IRepository<OutgoingEmail, Guid> emails` as the
  parameter **just before** `TimeProvider` (the unit test constructs it in that order). When the decision is made
  (`Outcome = Decided`) **and** `Keep` is true, add
  `OutgoingEmail.Queue(EmailKind.General, EmailTemplates.ForwardKept, forward.Email, $$"""{"shareLinkId":"{{link.Id:D}}"}""", now)`.
  Nothing on Block, nothing on a second press. Only `Add` may be called on the email repository (the test checks its
  received calls).
- Admin list: `ListOutgoingEmailsQuery(EmailStatus? Status, CursorRequest Page) : IQuery<CursorResult<OutgoingEmailDto>>, IAuthorizedRequest`
  (`RequiredRoles => ShareLinkRules.AdminRoles`), validator for the page (reuse the cursor validator: size 1–100).
  Newest first by `(QueuedAt desc, Id desc)`, keyset in SQL through a specification + `IPagedReader.CursorPageAsync`.
  `OutgoingEmailDto`: `Id, Kind, Template, Recipient (string, masked: email.To.ToString()), Status, Attempts, Provider, LastError, QueuedAt, LastAttemptAt, SentAt`.
  The masked recipient cannot be projected in SQL (value converter); project the raw value and mask in the Mapperly
  mapper / after paging, never send the full address.

### 1.4 Infrastructure — `src/SsSport.Drive.Infrastructure/Email/`

- Delete `EmailCodeSender.cs`. Keep `EmailOptions` / `EmailProviderOptions`; add
  `public SmtpOptions GoogleSmtp { get; init; } = new();` and
  `public sealed record SmtpOptions { Host = "smtp.gmail.com"; Port = 587; UserName = "info@ss-sport.co"; AppPassword = ""; SenderName = "SS-SPORT"; SenderEmail = "info@ss-sport.co"; bool IsConfigured => !string.IsNullOrWhiteSpace(AppPassword); }`.
  Config keys: `Email__GoogleSmtp__AppPassword` etc. Add them (empty) to `.env.example`.
- `EmailTransportException : Exception` (message like `"Resend refused the email with 422."`, never the address).
- `ResendTransport(IOptions<EmailOptions>, HttpClient)`, `BrevoTransport(IOptions<EmailOptions>, HttpClient)`: same
  HTTP bodies as the old `EmailCodeSender` but from `EmailMessage`; non-2xx → `EmailTransportException` containing the
  numeric status. `IsConfigured` = provider key set.
- `GoogleSmtpTransport(IOptions<EmailOptions>)`: MailKit (`MailKit` package, MIT; add to `Directory.Packages.props`),
  STARTTLS on 587, user/app password, multipart text + HTML. `IsConfigured` = app password set.
- `PickupDirectoryTransport(IOptions<EmailOptions>)`: writes `.eml` (From, To, Subject, Date, text body) into
  `PickupDirectory`; `IsConfigured` = directory set. Production still refuses the pickup directory (existing validation).
- `EmailDispatcher(IEnumerable<IEmailTransport>, ILogger<EmailDispatcher>) : IEmailDispatcher`:
  - a configured transport named `PickupDirectory` takes every email;
  - else route `Code` → `["Resend", "Brevo"]`, `General` → `["GoogleSmtp"]` (in that order, whatever the
    registration order), keeping only configured ones;
  - none → `InvalidOperationException` whose message names the settings (`Email__Resend__ApiKey`, `Email__Brevo__ApiKey`, `Email__GoogleSmtp__AppPassword`, `Email__PickupDirectory`);
  - try in order; `OperationCanceledException` propagates at once; any other exception → Warning log (provider name,
    exception type), try next; all failed → `InvalidOperationException("Every email provider failed …")`;
  - Information log on success with provider and masked recipient (`EmailAddress.ToString()` is masked).
- DI (`DependencyInjection.cs`): register the four transports as `IEmailTransport` (Resend/Brevo with the shared
  `HttpClient` as today), `IEmailDispatcher`, `IWebLinks` with `WebOptions { PublicBaseUrl }` bound from `Web`
  (validated: absolute URI; `https` required outside Development/Testing hosts; no trailing slash handling bugs).
- Persistence: `OutgoingEmailConfiguration` → table `outgoing_emails` (snake_case like the others), `to` through the
  existing `EmailAddress` converter, `data` as `jsonb`, enums as strings, index `(queued_at desc, id desc)` and
  `(status, queued_at)`. Add `DbSet`/model registration the way the other aggregates are registered. Migration:

```bash
dotnet ef migrations add AddOutgoingEmails --project src/SsSport.Drive.Infrastructure --output-dir Persistence/Migrations
```

- Hangfire: route `EmailQueued` events to a new job `EmailJob` on queue **`emails`** (`[Queue("emails")]`,
  `[AutomaticRetry(Attempts = 3)]`), running `IBackgroundEventRunner` like `BotNoticeJob`. Do it in
  `HangfireBackgroundEventQueue.EnqueueAsync` by event type. Start a second `BackgroundJobServer` in the API for the
  `emails` queue with **2 workers** (proposed) next to the notices server (one worker, unchanged). The inline test queue
  (`UseInlineBotNotices`) already runs every background event inline, so tests need nothing more.
- Gate purge (L-2b): **do not** purge `outgoing_emails` in this task (retention to be decided with the owner; see §6).

### 1.5 API

- `src/SsSport.Drive.Api/Emails/EmailQueueEndpoints.cs`: `GET /admin/emails?status=&cursor=&pageSize=` → 200
  `CursorResult<OutgoingEmailDto>`; tag `"Email queue"`; name, summary, description, example, 400/401/403 ProblemDetails.
  Map it in `Program.cs` next to the others. Enums serialise by name already.
- Add `Web__PublicBaseUrl` to `.env.example` and `appsettings.json` (empty / `http://localhost:3000` for dev) and
  document it in decisions §3a.

### 1.6 Phase 1 done when

`OutgoingEmailTests`, `EmailTransportTests`, `EmailDispatcherTests`, `SendQueuedEmailTests`, `ForwardKeptEmailTests`,
`EmailQueueEndpointTests` and the whole `ViewerGateEndpointTests` class are green. Commit:
"Send every email through a Hangfire queue with provider failover (L-4) and email kept forwards (L-3b)".

---

## Phase 2 — Magic-link sign-in: admin console (A-1, SSD-68) + viewers (P-5, SSD-96)

Decisions §7a: admin magic link → 7-day session; viewer magic link → 1-day session; viewers have no dashboard
(signing in only skips the email/OTP when requesting access, P-7). §1b: tokens stored as keyed hashes, constant-time
compare, `__Host-` cookies set by the web app, CSRF on cookie requests, suspicious activity logged.

### 2.1 Domain — `src/SsSport.Drive.Domain/Accounts/`

```csharp
public enum SignInAudience { Admin, Viewer }
public sealed class SignInLink : AggregateRoot<Guid>
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);   // (proposed)
    public SignInAudience Audience { get; }  public EmailAddress Email { get; }  public string? TokenHash { get; private set; }
    public DateTimeOffset CreatedAt { get; }  public DateTimeOffset ExpiresAt { get; private set; }  public DateTimeOffset? UsedAt { get; private set; }
    public static SignInLink Issue(SignInAudience audience, EmailAddress email, DateTimeOffset now);   // ExpiresAt = now + Lifetime
    public bool Arm(string tokenHash, DateTimeOffset now);   // throws ArgumentException on blank; false if used or now >= CreatedAt + Lifetime; else TokenHash = hash, ExpiresAt = now + Lifetime
    public bool Redeem(DateTimeOffset now);                  // false if TokenHash null, used, or now >= ExpiresAt; else UsedAt = now
}
public sealed class AccountSession : AggregateRoot<Guid>
{
    public static TimeSpan LifetimeOf(SignInAudience audience);   // Admin 7 days, Viewer 1 day
    public SignInAudience Audience { get; }  public Guid SubjectId { get; }  public EmailAddress Email { get; }  public string TokenHash { get; }
    public DateTimeOffset CreatedAt { get; }  public DateTimeOffset ExpiresAt { get; }  public DateTimeOffset? EndedAt { get; private set; }
    public static AccountSession Start(SignInAudience audience, Guid subjectId, EmailAddress email, string tokenHash, DateTimeOffset now); // blank hash or Guid.Empty subject → ArgumentException
    public bool IsActiveAt(DateTimeOffset now);   // EndedAt is null && now < ExpiresAt
    public void End(DateTimeOffset now);          // first call wins
}
public sealed class ViewerAccount : AggregateRoot<Guid>
{
    public EmailAddress Email { get; }  public DateTimeOffset CreatedAt { get; }  public DateTimeOffset LastSignedInAt { get; private set; }
    public static ViewerAccount Register(EmailAddress email, DateTimeOffset now);
    public void SignedIn(DateTimeOffset now);
}
```

`SubjectId` is the `User.Id` for admins and the `ViewerAccount.Id` for viewers.

### 2.2 Application — `src/SsSport.Drive.Application/Accounts/SignIn.cs`

Reuse `IViewerSecrets` (`NewToken`, `HashToken`) for tokens and hashes (keyed HMAC, already constant-time compared via
hash lookup). All commands are `ICommand<…>`, with FluentValidation validators.

- `RequestSignInLinkCommand(SignInAudience Audience, string? Email) : ICommand<Unit>`
  - Validator: email required and valid (`EmailAddress.Create` rules) → 400.
  - Admin: only when a `User` with that email `IsAdmin` (active + Admin role) exists. Viewer: any email.
  - Cap **(proposed)**: at most 5 links per email and audience in the last hour (count `SignInLink` rows by spec). Over
    the cap → do nothing. Always return success: the endpoint answers **202** in every case (no probing).
  - Otherwise add `SignInLink.Issue(...)` and `OutgoingEmail.Queue(EmailKind.Code (proposed: auth mail goes through
    the fast code providers), EmailTemplates.AdminSignIn | ViewerSignIn, email, {"signInLinkId":"<guid>"}, now)`.
  - Log a Warning (no email) when an admin link is asked for an address that is not an admin (suspicious activity).
- `AdminSignInComposer` / `ViewerSignInComposer` (`IEmailComposer`): load the `SignInLink`; draw `secrets.NewToken()`;
  `Arm(secrets.HashToken(token), now)`; false → `null`; save; return the email with `webLinks.AdminSignIn(token)` /
  `webLinks.ViewerSignIn(token)` in both text and HTML. The token never touches the request, job args, data or logs.
- `RedeemSignInLinkCommand(SignInAudience Audience, string? Token) : ICommand<SignInSessionDto>`
  - Find the link by `(Audience, TokenHash = HashToken(token))`; missing, wrong audience or `Redeem` false →
    `SignInInvalidException` → 401 ProblemDetails `code = "sign_in.invalid"`; log Warning.
  - Admin: the user with that email must still be an active admin, else the same 401.
  - Viewer: find or `Register` the `ViewerAccount` by email; `SignedIn(now)` when it exists.
  - Start an `AccountSession` with a fresh token; return `SignInSessionDto(SessionToken, ExpiresAt, Email, Audience)`.
- `GetSignedInQuery(SignInAudience Audience, string? SessionToken) : IQuery<SignedInDto>` →
  `SignedInDto(Email, DisplayName?, Audience, ExpiresAt)` (DisplayName = the admin's display name; null for viewers).
  No/unknown/ended/expired session, wrong audience, or (admin) a user no longer active admin → 401
  `code = "sign_in.required"`.
- `SignOutCommand(SignInAudience Audience, string? SessionToken) : ICommand<Unit>` → ends the session if found; always 204.
- New exceptions in `ApplicationExceptions.cs` (`SignInInvalidException`, `SignInRequiredException`) mapped to 401 with
  those codes in `ApplicationExceptionHandler`.

### 2.3 Infrastructure

- EF configurations + one migration (`AddSignIn`): tables `sign_in_links` (unique index on `token_hash` where not
  null; index `(email, audience, created_at)`), `account_sessions` (unique `token_hash`), `viewer_accounts` (unique
  `email`). Enums as strings.
- Purge (extend the L-2b `gate-purge` job, same batching): sign-in links 1 day after they expire, account sessions
  7 days after they end or expire **(proposed)**. Viewer accounts are kept.

### 2.4 API

- `src/SsSport.Drive.Api/Accounts/SignInEndpoints.cs`, tag `"Sign-in"`, for `{audience}` in `admin` and `viewer`
  (map two explicit route sets, not a free route value):
  - `POST /auth/{audience}/link` `{ "email": "…" }` → 202 (empty body).
  - `POST /auth/{audience}/session` `{ "token": "…" }` → 200 `SignInSessionDto` | 401 `sign_in.invalid`.
  - `GET /auth/{audience}/me` → 200 `SignedInDto` | 401 `sign_in.required`.
  - `POST /auth/{audience}/sign-out` → 204.
  - Session header: `X-Admin-Session` for admin, `X-Viewer-Account` for viewer. The API never reads cookies.
  - **CSRF / content type:** every `POST /auth/*` accepts only `application/json` bodies; anything else → 415
    (`.Accepts<T>("application/json")` is not enough on its own; reject in an endpoint filter). The web app's
    same-origin routes check `Origin` and set `__Host-ssd_as` (7 d) / `__Host-ssd_va` (1 d) cookies; that web part is a
    follow-up (section 5), not this branch.
  - Full Scalar docs: names, summaries, descriptions, examples, 202/200/204/400/401/415/429 responses.
- **Admin session authentication**: add `AdminSessionAuthenticationHandler` (scheme `AdminSession`) reading
  `X-Admin-Session`: valid live Admin `AccountSession` whose user is still an active admin → the **same claims** the
  Mini App handler issues (`NameIdentifier` = user id, `Role`, `telegram_id`). No header → `NoResult`; bad/expired →
  `Fail`. Combine with the Mini App scheme through a policy scheme (`ForwardDefaultSelector`: `X-Admin-Session`
  present → `AdminSession`, else `TelegramMiniApp`) and make it the default, so every existing `/admin/*` endpoint and
  `ICurrentUser` work with either. A viewer account session in `X-Admin-Session` must fail (audience check).
- Rate limit: new budget `RateLimiting:AuthPermitsPerMinute` (default **10**, proposed) partitioned per address for
  paths under `/auth`, in `ApiRateLimiting` (add to options, validation and the partition switch). Rate-limit hits are
  already logged as warnings.

### 2.5 Phase 2 done when

`SignInTests` and `SignInEndpointTests` are green (and Phase 1 stays green). Commit:
"Add email magic-link sign-in for admins (A-1) and viewers (P-5)".

---

## Phase 3 — Watch tracking (L-5, SSD-61) + metrics (L-6, SSD-62)

### 3.1 Domain — `src/SsSport.Drive.Domain/Tracking/`

```csharp
public readonly record struct WatchRange(double From, double To)
{
    public double Length => To - From;
    public static WatchRange Create(double from, double to);   // ArgumentOutOfRangeException unless finite, 0 <= from < to
}
public static class WatchMetrics
{
    public const double MergeGap = 0.5;               // (proposed) heartbeat jitter
    public const double CompletionThreshold = 0.9;    // (proposed) complete from 90 %
    public const double HotspotBucketSeconds = 5;     // (proposed)
    public static IReadOnlyList<WatchRange> Merge(IEnumerable<WatchRange> ranges);   // sort by From; join when next.From <= current.To + MergeGap
    public static double UniqueSeconds(IEnumerable<WatchRange> ranges);              // sum of merged lengths
    public static double WatchSeconds(IEnumerable<WatchRange> ranges);               // sum of raw lengths
    public static double Completion(IEnumerable<WatchRange> ranges, double durationSeconds); // merged ranges clipped to [0, duration]; unique / duration, capped at 1; 0 when duration <= 0
    public static bool IsComplete(IEnumerable<WatchRange> ranges, double durationSeconds);   // Completion >= CompletionThreshold
    public static IReadOnlyList<int> Hotspots(IEnumerable<WatchRange> ranges, double durationSeconds, double bucketSeconds = HotspotBucketSeconds);
    // bucket i = [i*b, (i+1)*b); count of raw ranges with overlap > 0 (touching an edge does not count);
    // ceil(duration / b) buckets; none when duration <= 0; bucketSeconds <= 0 → ArgumentOutOfRangeException
}
public enum PlaybackEventType { Play, Pause, Seek, Ended }
public sealed class WatchSession : AggregateRoot<Guid>
{
    public const double MaxHeartbeatSeconds = 30;   // (proposed) 10 s heartbeat at 2x speed + slack
    public const int MaxRanges = 2000;
    public Guid ViewerSessionId, ShareLinkId, PlayerId, AssetId { get; }  public EmailAddress Email { get; }
    public double DurationSeconds { get; }  public int Plays { get; private set; }  public IReadOnlyList<WatchRange> Ranges { get; }
    public DateTimeOffset StartedAt { get; }  public DateTimeOffset LastSeenAt { get; private set; }
    public double UniqueSeconds => …; public double WatchSeconds => …; public double Completion => …; public bool IsComplete => …;
    public IReadOnlyList<int> Hotspots(double bucketSeconds = WatchMetrics.HotspotBucketSeconds);
    public static WatchSession Start(Guid viewerSessionId, Guid shareLinkId, Guid playerId, Guid assetId, EmailAddress email, double durationSeconds, DateTimeOffset now); // durationSeconds <= 0 → ArgumentOutOfRangeException; Plays = 1
    public bool Heartbeat(double from, double to, DateTimeOffset now);
    public void RecordEvent(PlaybackEventType type, double positionSeconds, DateTimeOffset now);
}
public sealed class TrackingEvent : AggregateRoot<Guid>
{
    public Guid WatchSessionId { get; }  public PlaybackEventType Type { get; }  public double PositionSeconds { get; }  public DateTimeOffset OccurredAt { get; }
    public static TrackingEvent Record(Guid watchSessionId, PlaybackEventType type, double positionSeconds, DateTimeOffset now); // id = Guid.CreateVersion7(now)
}
```

`Heartbeat` rules (see `WatchSessionTests`): refuse (return false, change nothing) when not finite, `from < 0`,
`to <= from`, `to - from > MaxHeartbeatSeconds`, `to > DurationSeconds + 1`, or `Ranges.Count == MaxRanges` and the
range does not extend the last one. Clip `to` to `DurationSeconds`. When `|from - last.To| <= WatchMetrics.MergeGap`
extend the last range to `max(last.To, to)`; else append. Set `LastSeenAt = now`.
`RecordEvent`: `Play` after the previous event `Ended` → `Plays++`; every event sets `LastSeenAt = now` and remembers
the last event type. Note: `Hotspots` counts **raw** ranges, so keep `Ranges` raw (not merged).

### 3.2 Application — `src/SsSport.Drive.Application/Tracking/`

Same check order as `ViewerContent.cs`: link does not open → `ShareLinkUnavailableException` (404
`share_link.unavailable`); no live `ViewerSession` for this link → `ViewerVerificationRequiredException` (401
`viewer.verification_required`); then the specific 404s. Reuse the existing viewer-session lookup helpers.

- `StartWatchCommand(string Code, string? SessionToken, Guid PlayerId, Guid AssetId) : ICommand<WatchStartedDto>`:
  player must be on the link; asset must be that player's **ready, visible video** with a duration → else 404
  `tracking.unknown_video` (`NotFoundException` with that code, or a new exception type mapped to 404 with the code).
  Creates `WatchSession.Start(session.Id, link.Id, player, asset, session.Email, asset.DurationSeconds!.Value, now)`.
  Returns `WatchStartedDto(Guid WatchId, int HeartbeatSeconds = 10)`.
- `RecordHeartbeatCommand(string Code, string? SessionToken, Guid WatchId, double From, double To) : ICommand<Unit>`:
  the watch must exist **and** belong to this viewer session → else 404 `tracking.unknown_watch`.
  `Heartbeat` false → 422 `tracking.invalid_range` (a `DomainException` with that code is the simplest: it already maps
  to 422). Log refused heartbeats at Information with the watch id only.
- `RecordPlaybackEventCommand(string Code, string? SessionToken, Guid WatchId, PlaybackEventType? Type, double Position) : ICommand<Unit>`:
  validator: `Type` required and a defined enum value (an unknown JSON string must give **400**, not 500: bind the body
  as a string `type` in a form DTO and parse it in the validator); position finite and ≥ 0. Same watch checks. Calls
  `RecordEvent` and adds `TrackingEvent.Record(...)`.
- `GetLinkViewingQuery(Guid ShareLinkId, PageRequest Page) : IQuery<PagedResult<ViewingRowDto>>, IAuthorizedRequest`
  (admins only). One row per `(Email, AssetId)` of the link: `Email` (full: admins may see viewers, decisions §7a),
  `PlayerId`, `AssetId`, `Plays` (sum), `WatchSeconds` (sum of raw), `UniqueSeconds` and `Completion` (from **all**
  that viewer's ranges for the asset merged), `IsComplete`, `LastSeenAt` (max). Page the **groups** in SQL
  (`GROUP BY email, asset_id ORDER BY max(last_seen_at) DESC, asset_id`, `Skip/Take`, total count of groups), then load
  only that page's watch rows to compute merged metrics. Never load the whole table. Unknown link → 404.

### 3.3 Infrastructure

- EF configurations + one migration (`AddWatchTracking`): `watch_sessions` (`ranges` as `jsonb` array of
  `{from,to}` via a value converter + value comparer; index `(share_link_id, email, asset_id)` and
  `(viewer_session_id)`), `tracking_events` (index `(watch_session_id, id)`). Store `Plays`, `DurationSeconds`,
  `StartedAt`, `LastSeenAt`, `Email` (existing converter). Do not persist computed properties.
- The L-2b purge deletes viewer sessions after 7 days: watch rows keep their own data (no FK cascade from
  `viewer_sessions`; store the id only).

### 3.4 API

- In `ViewerContentEndpoints.cs` (or a new `TrackingEndpoints.cs`), tag `"Viewer tracking"`, all with `X-Viewer-Session`:
  - `POST /links/{code}/players/{playerId:guid}/videos/{assetId:guid}/watch` → 201 `WatchStartedDto` (Location may be omitted).
  - `POST /links/{code}/watch/{watchId:guid}/heartbeat` `{ "from": 0, "to": 10 }` → 204.
  - `POST /links/{code}/watch/{watchId:guid}/events` `{ "type": "Play|Pause|Seek|Ended", "position": 12.5 }` → 204.
- Rate limit: these tracking POSTs use the **viewer** budget (`RateLimiting:ViewerPermitsPerMinute`), not the gate's
  30/min: extend `IsViewerContentRead`-style matching in `ApiRateLimiting` for the three tracking paths.
- `GET /admin/links/{id:guid}/viewing?page=&pageSize=` → 200 `PagedResult<ViewingRowDto>`, tag `"Share links"`.
- Full Scalar docs with examples for all four.

### 3.5 Phase 3 done when

`WatchMetricsTests`, `WatchSessionTests`, `WatchEndpointTests` green; Phases 1–2 still green. Commit:
"Track watches with heartbeats (L-5) and compute viewing metrics (L-6)".

---

## 4. Finish (after all three phases)

1. Full suite, real result, nothing skipped:

```bash
dotnet build SsSport.Drive.slnx -warnaserror
```

```bash
dotnet test --solution SsSport.Drive.slnx
```

2. Housekeeping (CLAUDE.md "Keep up to date"):
   - `.claude/decisions.md`: add sections for L-4 (queue, kinds, retries, `emails` queue + 2 workers, admin page API,
     data rules), L-3b, A-1/P-5 (lifetimes, headers, 202 no-probe, caps, auth rate limit, purge) and L-5/L-6 (endpoints,
     heartbeat rules, metric definitions, thresholds). Mark every **(proposed)** value as "proposed, waiting for the
     owner" until confirmed. Add `Web__PublicBaseUrl`, `Email__GoogleSmtp__*`, `RateLimiting__AuthPermitsPerMinute`
     to §3a.
   - `.env.example`, `README.md` (stack: MailKit; structure: Emails/Accounts/Tracking; roadmap status),
     `.gitignore` / `.dockerignore` if anything new appears.
   - `.claude/backlog.md`: no rows change status there (it has no status column); add new follow-up rows from §5.
   - Every new endpoint fully documented in Scalar (tests in `ApiDocumentationTests` must still pass).
3. Commit, push the branch, open a PR into `main` with the §6 list in its description. CI must pass.
4. Jira (`bonyadin2000.atlassian.net`, project SSD): for SSD-60, SSD-130, SSD-68, SSD-96, SSD-61, SSD-62 add a comment
   (branch, commits, test result) and move each to **Done** (transition id `31`) only after the PR's CI is green.
   SSD-68 note: the console's web pages and cookies are a follow-up (§5), so say so in the comment.
5. Remind the owner to run k6: heartbeat ingest (K-3), auth endpoints, email queue admin list; offer to write the scripts.

## 5. Follow-ups to add to backlog.md and Jira (new tasks)

- Web: `/admin/sign-in` and `/sign-in` pages + same-origin routes that set `__Host-ssd_as` / `__Host-ssd_va` and
  forward the headers (A-1/P-5 web part), home page Sign in / Sign out (P-7 needs it).
- Web: player sends watch start / heartbeat every 10 s / events (L-5 client part, in the V-3 player).
- Email queue admin page in the console (A-9) on top of `GET /admin/emails`.
- Retention for `outgoing_emails` and `tracking_events` (owner to decide).

## 6. Owner decisions to confirm (put this list in the PR)

1. Magic links last **15 minutes**; at most **5 per email per hour** per audience.
2. Sign-in emails go through **Resend → Brevo** (like codes), not Google SMTP.
3. `/auth/*` per-address budget **10/min**.
4. Email queue: **2 workers**, **4 attempts** in all (Hangfire default backoff between retries).
5. Purge: sign-in links 1 day after expiry, account sessions 7 days after they end.
6. Tracking: heartbeat every **10 s**, at most **30 s** per heartbeat, ranges within **0.5 s** join, video complete at
   **90 %**, hotspots in **5 s** buckets, a new play only after the video ended.
7. Admins see viewers' full emails in the viewing metrics (§7a "everything is logged per viewer").

## 7. Pitfalls

- `EmailAddress.ToString()` is masked; use `.Value` only where the full address is required (sending, admin viewing).
- The test host replaces all `IEmailTransport` registrations with three recording ones named `Resend`, `Brevo`,
  `GoogleSmtp`; the dispatcher must route by **name**, not by type.
- `UseInlineBotNotices` runs background events inline **inside the request**: the email job's own `SaveChangesAsync`
  runs in a new scope (the runner creates one); do not share the request's DbContext.
- `ForwardKeptEmailTests` constructs `DecideForwardCommandHandler` positionally:
  `(users, forwards, links, devices, sessions, audit, emails, timeProvider)`.
- `SendQueuedEmailTests` constructs `SendQueuedEmail` positionally:
  `(emails, unitOfWork, composers, dispatcher, timeProvider, logger)`.
- Keep `OutgoingEmail` id time-ordered (`Guid.CreateVersion7(now)`) so `(queued_at desc, id desc)` is stable.
- New aggregates must be registered wherever the architecture tests expect (DTO boundary, paging attributes):
  run `tests/SsSport.Drive.ArchitectureTests` early.
