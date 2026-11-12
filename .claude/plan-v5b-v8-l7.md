# Plan: signed media (V-5b), DataVolley stats (V-8), admin alerts and daily digest (L-7)

Written 2026-11-12 (Opus) for an implementing model (Sonnet). Branch `feature/viewer-stream-scout-digest`, cut from
`main` at `68780ef`. **The tests are already written and committed red.** Your job: make every test green without
weakening one, following the phases below in order. These three tasks close **Epic 4** (V-5b SSD-118, V-8 SSD-101)
and, together with the other branch's L-4/L-5/L-6, **Epic 5** (L-7 SSD-63).

Owner decisions behind this plan are in `.claude/decisions.md`: §6 "V-5b plan", §4a "V-8 decision", §7 "Admin bot
alerts and daily digest (L-7)". Read those three paragraphs first.

---

## 0. Before you start

- Read `.claude/CLAUDE.md`. Everything there applies:
  - MediatR for every command and query.
  - Specification pattern for query conditions, and `AsNoTracking` projections on reads.
  - Mapperly for DTOs.
  - XML doc comments on **every** member, public and private, tests too. TSDoc on every exported TS function and component.
  - English only. UI copy lives in `copy.ts` files (web) and `BotTexts.resx` (bot).
  - Logging with source-generated Serilog `[LoggerMessage]`, never an email, token or code.
  - Lists paged in SQL.
  - Scalar docs complete for every new endpoint.
- **Never edit a test to make it pass.** If a test truly contradicts `decisions.md`, stop and ask the owner. The only
  existing tests changed on purpose are `edge/src/index.test.ts` (unknown paths now go to the site) and
  `web/src/lib/viewer/content-server.test.ts` (new page fields). Both are already done.
- Values marked **(proposed)** are Opus's proposals. They are what the tests expect. List them in the PR description so
  the owner can confirm them.
- Jira: move SSD-118, SSD-101 and SSD-63 to *In Progress* when you start, and comment on and close each one when its
  phase is green and committed (see `.claude/CLAUDE.md` "Backlog & Jira").

### Closest models to copy

| What | Copy from |
|---|---|
| Viewer content query, order of checks, error codes | `src/SsSport.Drive.Application/Links/ViewerContent.cs` |
| Viewer endpoints, Scalar docs, rate-limit policy | `src/SsSport.Drive.Api/Links/ViewerContentEndpoints.cs` |
| Paged list in SQL | `GetLinkPlayerDocumentsQueryHandler` + `IPagedReader.PageAsync` |
| Facts read in one projection | `src/SsSport.Drive.Infrastructure/Persistence/LinkPlayerReader.cs` |
| Aggregate with time-ordered id and an event | `src/SsSport.Drive.Domain/Links/LinkForward.cs` |
| Background event handler → admins | `NotifyAdminsOfForward` in `src/SsSport.Drive.Application/Telegram/Links/ForwardAlerts.cs` |
| Notice through the T-8 rules | `IBotNoticeOutbox.QueueAsync` in `src/SsSport.Drive.Application/Telegram/Notices/BotNotices.cs` |
| Recurring Hangfire job | `GatePurgeJob` + `RegisterRecurringJobs` in `src/SsSport.Drive.Infrastructure/BackgroundJobs/BotNotices.cs` |
| EF configuration | `src/SsSport.Drive.Infrastructure/Persistence/Configurations/ViewerGateConfiguration.cs` |
| Next.js forwarding route | `web/src/app/api/links/[code]/players/[playerId]/photo/route.ts` + `forwardPhoto` |
| Dialog behaviour (focus, Escape, scroll lock, overlay) | `web/src/components/viewer/document-viewer/document-viewer.tsx` |

### Test files (all red now)

| Phase | Test file | Why red now |
|---|---|---|
| 1 | `tests/SsSport.Drive.Api.IntegrationTests/Links/ViewerMediaEndpointTests.cs` (V-5b part) | endpoints missing (404) |
| 2 | `edge/src/routing.test.ts`, `edge/src/index.test.ts` | no pass-through, no `/pages` |
| 3 | `web/src/lib/viewer/media-server.test.ts`, `media-client.test.ts` | modules missing |
| 3 | `web/src/components/viewer/player/use-signed-source.test.tsx` | hook missing |
| 3 | `web/src/components/viewer/player-page/signed-media.test.tsx` | sections do not sign |
| 3 | `web/src/lib/viewer/content-server.test.ts` (2 cases) | page lacks paths and scouts |
| 4 | `tests/SsSport.Drive.Application.UnitTests/Scouting/DataVolleyFileTests.cs`, `ScoutStatsTests.cs` | `Application.Scouting` missing |
| 4 | `ViewerMediaEndpointTests.cs` (V-8 part) | endpoints missing |
| 5 | `web/src/lib/viewer/scout.test.ts`, `web/src/components/viewer/scout/scout-report.test.tsx` | modules missing |
| 6 | `tests/SsSport.Drive.Domain.UnitTests/Links/LinkOpenTests.cs` | `LinkOpen` missing |
| 6 | `tests/SsSport.Drive.Api.IntegrationTests/Telegram/AdminAlertsTests.cs` (first-open part) | |
| 7 | `tests/SsSport.Drive.Application.UnitTests/Telegram/DailyDigestTests.cs` | `Telegram.Digest` missing |
| 7 | `AdminAlertsTests.cs` (digest part) | |

C# test projects compile as a whole: nothing in a project runs until **every** type it references exists. So:

### 0.1 Stubs first (one commit, no behaviour)

Create every new public type and member named in sections 1.1, 4.1, 6.1 and 7.1, with `throw new NotImplementedException()`
bodies, so `dotnet build SsSport.Drive.slnx` is clean. Then fix any compile error the tests still show by matching the
tests exactly (names, parameter order, record shapes). Commit: "Add stubs for signed media, scout stats and admin alerts".

### Running tests

```bash
dotnet test --project tests/SsSport.Drive.Application.UnitTests -- --filter-namespace "SsSport.Drive.Application.UnitTests.Scouting"
dotnet test --project tests/SsSport.Drive.Api.IntegrationTests -- --filter-class "SsSport.Drive.Api.IntegrationTests.Links.ViewerMediaEndpointTests"
cd edge && npx vitest run
cd web && npx vitest run && npm run typecheck && npm run lint
```

Integration tests need Docker running (Testcontainers PostgreSQL).

### Dependency on the other branch (read before Phase 7)

`feature/backend-tracking-email-signin` (another session) builds L-4 (email queue, `OutgoingEmail`), L-5 (watch
tracking) and L-6 (metrics). Phases 1 to 7a do **not** need it. **Phase 7b** (plays, minutes, top players and failed
emails in the digest) waits until that branch is merged into `main`. Then rebase onto `main` and do 7b. If it is not
merged when you reach Phase 7b, open the PR with 1–7a done and say 7b is pending. L-7 stays *In Progress* in Jira until
then.

---

## Phase 1 — V-5b API: signed stream and page URLs (SSD-118)

### 1.1 Application — `src/SsSport.Drive.Application/Links/ViewerMedia.cs`

```csharp
public static class MediaFolders                         // (proposed) where I-3 / I-4 write
{
    public const string MasterPlaylist = "master.m3u8";
    public static string VideoHls(Guid playerId, Guid videoId, int version)        // players/{p}/videos/{v}/v{version}/hls
    public static string DocumentPages(Guid playerId, Guid documentId, int version) // players/{p}/documents/{d}/v{version}/pages
}
public sealed record VideoStreamDto   { public string PlaylistUrl { get; init; } public DateTimeOffset ExpiresAt { get; init; } }
public sealed record DocumentPagesDto { public IReadOnlyList<string> Pages { get; init; } = []; public DateTimeOffset ExpiresAt { get; init; } }
public sealed class ViewerVideoSpec(Guid playerId, Guid videoId)       : Specification<Asset>, ISingleResultSpecification<Asset>
public sealed class ViewerDocumentSpec(Guid playerId, Guid documentId) : Specification<Asset>, ISingleResultSpecification<Asset>
public sealed record GetVideoStreamQuery(string Code, string? SessionToken, Guid PlayerId, Guid VideoId) : IQuery<VideoStreamDto>;
public sealed record GetDocumentPagesQuery(string Code, string? SessionToken, Guid PlayerId, Guid DocumentId) : IQuery<DocumentPagesDto>;
```

- Specs: the asset's `PlayerId`, `Id`, kind (`Video` / `Document`), `Visibility == Visible`, `State == Ready`, `AsNoTracking`.
  Reuse the conditions of `ViewerVideosSpec` / `ViewerDocumentsSpec`, without repeating them (DRY: e.g. a shared private
  expression or a base spec).
- Both handlers run the same checks, in the same order as `ViewerContent.cs`:
  1. `links.OpenLinkAsync(code, now)`: a closed or unknown link is 404 `share_link.unavailable`.
  2. `sessions.RequireSessionAsync(...)`: no live session for this link is 401 `viewer.verification_required`.
  3. `link.EnsureShows(playerId)`: a player not on the link is 404.
  4. The asset spec: no match is `NotFoundException("Video")` / `("Document")`.
  5. `if (!issuer.IsEnabled) throw new ServiceUnavailableException("media.streaming_off", "Video streaming is not set up.")`.
- Stream: `issuer.Issue(MediaFolders.VideoHls(player, asset.Id, asset.Version), sessionToken)`. Answer its
  `PlaylistUrl` and `ExpiresAt`. Its URL ends in `/master.m3u8`; check that it equals `MediaFolders.MasterPlaylist`.
- Pages: one token from `issuer.Issue(MediaFolders.DocumentPages(...), sessionToken).Token`. Then
  `Pages = 1..PageCount → $"/pages/{token}/{folder}/{n}.webp"`. `PageCount` null means `[]`. `ExpiresAt` comes from the
  same access.
- `IHlsTokenIssuer` gains `bool IsEnabled { get; }` (XML doc: false when `Edge__HlsSigningKey` is empty).
  `HmacHlsTokenIssuer.IsEnabled => EdgeOptions.IsValidSigningKey(options.Value.HlsSigningKey)`. Add one test for it to
  `HmacHlsTokenIssuerTests` (adding a test is fine).

### 1.2 API — `src/SsSport.Drive.Api/Links/ViewerMediaEndpoints.cs` (`MapViewerMedia()`, call it in `Program.cs` after `MapViewerContent()`)

| Route | Name | Answers |
|---|---|---|
| `GET /links/{code}/players/{playerId:guid}/videos/{videoId:guid}/stream` | `GetVideoStream` | 200 `VideoStreamDto`, 401, 404, 503 |
| `GET /links/{code}/players/{playerId:guid}/documents/{documentId:guid}/pages` | `GetDocumentPages` | 200 `DocumentPagesDto`, 401, 404, 503 |

- The session comes from the `X-Viewer-Session` header (`ViewerGateEndpoints.SessionHeader`).
- Set `Cache-Control: no-store` on every answer (success too; the tests check it).
- Use the same rate-limit policy as the other viewer content reads (`ViewerPermitsPerMinute`).
- Scalar: tag "Viewer content", name, summary, a description saying the token is bound to the session and lives 30
  minutes, a realistic example (`/hls/eyJ2Ijox….Q2x…/players/…/v1/hls/master.m3u8`), every ProblemDetails, and the
  viewer-session security scheme. Copy how `ViewerContentEndpoints` documents these.

**Green:** the V-5b tests in `ViewerMediaEndpointTests` (names start with `A_video_stream`, `The_stream_token`,
`Each_session`, `A_replaced_video`, `A_stream_needs`, `Only_a_watchable`, `Without_a_signing_key`, `Document_pages`,
`A_document_without`, `Only_a_readable`). Commit.

---

## Phase 2 — V-5b edge: short links pass through, signed pages (SSD-118)

In `edge/src/hls.ts` and `edge/src/index.ts`:

- **Token kinds.** Generalise `isAllowed` to take `kind: "hls" | "pages"`:
  - `hls`: the token's folder `p` must end in `/hls`, and the file must match today's `SERVABLE_FILE`;
  - `pages`: `p` must end in `/pages`, and the file must match `^[1-9][0-9]{0,3}\.webp$`.

  Everything else stays as today: signature, expiry, session hash, inside `p`, safe segments. The shared vectors keep
  passing; their folder already ends in `/hls`.
- **`/pages/{token}/{key}`**: GET/HEAD only (405 otherwise), bare 403 `Forbidden` on any failure without reading R2,
  404 when R2 has nothing. Headers: `Content-Type: image/webp`, `Cache-Control: private, max-age=1800`,
  `X-Content-Type-Options: nosniff`, `Content-Length`, `ETag`. Share the serving code with `serveHls` (DRY).
- **Routing order:** `/health`, then `/hls/…`, then `/pages/…`. Then `/r/…`: when the path is `/r/{x}` or
  `/r/{x}/p/{y}` with `x` not matching `^[ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789]{6}$` (the alphabet
  from `ShareLinkCode.Alphabet`; keep a comment saying so) or `y` not a GUID, answer the bare 404 with
  `Cache-Control: no-store`. **Everything else:** `return fetch(request)`, so the request goes to the origin unchanged
  (on Cloudflare the Worker's route sits on `drive.ss-sport.co/*`, so this reaches the Next.js origin).
- Update `edge/README.md` (pages route, pass-through, the `/r/` filter) and `wrangler.toml`'s comment (route
  `drive.ss-sport.co/*`, still commented until S-2).

**Green:** `cd edge && npx vitest run && npm run typecheck`. Commit.

---

## Phase 3 — V-5b web: sign when needed (SSD-118)

### 3.1 `web/src/lib/viewer/media-client.ts` (browser-safe)

- `streamPath(code, playerId, videoId)`, `pagesPath(code, playerId, documentId)`, `scoutPath(code, playerId, scoutId)`,
  `scoutsPath(code, playerId)`: `/api/links/{encodeURIComponent(code)}/players/{id}/…`.
- `fetchStreamUrl(path, fetchImpl = fetch): Promise<string>`. Uses `credentials: "same-origin"` and `cache: "no-store"`.
  Throws on a non-2xx answer, and throws when `playlistUrl` does not start with `/hls/`.
- `fetchPages(path, fetchImpl = fetch): Promise<string[]>`: keeps only entries that start with `/pages/`.
- `fetchScoutReport(path, fetchImpl = fetch): Promise<ScoutReport>`. On a non-2xx answer it throws an error object with
  a `status` property (an `Error` subclass, e.g. `MediaRequestError`).

### 3.2 `web/src/lib/viewer/media-server.ts` (server only)

- `forwardStream`, `forwardPages` and `forwardScoutReport` all take
  `(request, apiBaseUrl, linkCode, playerId, itemId, fetchImpl = fetch)`.
- Behaviour, as `forwardPhoto` does, but for JSON:
  - a non-GUID id answers 404 without calling the API;
  - no session cookie answers 401 without calling the API;
  - the API is called with `X-Viewer-Session` and `X-Forwarded-For`, and `cache: "no-store"`;
  - a network failure answers 502;
  - a non-2xx answer passes its status through with an **empty** body;
  - a 2xx answer passes the JSON through with `Cache-Control: no-store` and `Content-Type: application/json`.
- Move the shared bits out of `content-server.ts` so both files use them: `GUID`, `apiHeaders`, `linkBase` and the
  session read. Do not copy them.
- Route files call these (GET only, no CSRF check, the same as the photo route):
  - `web/src/app/api/links/[code]/players/[playerId]/videos/[videoId]/stream/route.ts`
  - `web/src/app/api/links/[code]/players/[playerId]/documents/[documentId]/pages/route.ts`
  - `web/src/app/api/links/[code]/players/[playerId]/scouts/[scoutId]/route.ts`

### 3.3 Page data (`content-server.ts`, `player-page/types.ts`)

- `PlayerVideo` gains `streamPath?: string`. `PlayerDocument` gains `pagesPath?: string`.
- `PlayerPageData` gains `scouts: PlayerScout[]` with `{ id, title, reportPath }`, and `stats.matches: number`.
- `toPlayerPage` fills `streamPath` / `pagesPath` / `reportPath` from the helpers in 3.1, keeping `src: ""` and `pages: []`.
- `readPlayer` also reads `/players/{id}/scouts?page=1&pageSize=100`, in parallel with videos and documents.
- Update `sample.ts` (the design preview) for the new required fields: `scouts`, `stats.matches`.

### 3.4 Components

- `web/src/components/viewer/player/use-signed-source.ts`:
  `useSignedSource(streamPath: string | undefined, fixedSrc: string): { src: string; refreshSource?: () => Promise<string>; failed: boolean }`.
  - Without a path, it returns `fixedSrc` and no refresh, and never fetches.
  - With a path, `src` stays `""` until the first `fetchStreamUrl(path)` resolves. `refreshSource` calls it again.
  - A new path signs again; ignore a stale answer for an old path.
  - A failure sets `failed`.
- `VideosSection`: use the hook for `current` and pass `src` and `refreshSource` to `VideoPlayer`. Sign **only the
  current video**, never the whole playlist. While `src` is `""`, keep the player's poster or idle look. When `failed`
  is true, show `pageCopy.videoUnavailable` = "This video cannot be played right now." in the player area.
- `DocumentsSection`: when the opened document has `pagesPath` and no `pages`, call `fetchPages`, then open
  `DocumentViewer` with those pages. Keep the card's focus return. On failure show
  `pageCopy.documentUnavailable` = "This document cannot be opened right now." as `role="alert"` near the cards.
  Documents with fixed `pages` (the sample) open at once.

**Green:** `cd web && npx vitest run` (all but the Phase 5 files), `npm run typecheck`, `npm run lint`. Commit. Then
close **V-5b** in Jira (comment: branch, commit, test results).

---

## Phase 4 — V-8 API: scout files and their stats (SSD-101)

### 4.1 Application — `src/SsSport.Drive.Application/Scouting/`

Pure code: no IO, no EF. These types are what the tests use:

```csharp
public enum ScoutSide { Home, Away }
public enum ScoutSkill { Serve, Reception, Attack, Block, Dig, Set, Freeball }
public enum ScoutEvaluation { Perfect, Positive, Ok, Negative, Poor, Error }   // # + ! - / =
public sealed record ScoutTeam(string Code, string Name, int SetsWon);
public sealed record ScoutSetScore(int Home, int Away);
public sealed record ScoutPlayer(ScoutSide Side, int Number, string? PlayerId, string LastName, string FirstName);
public sealed record ScoutAction(ScoutSide Side, int Number, ScoutSkill Skill, ScoutEvaluation Evaluation);
public sealed class ScoutMatch                       // Date (DateOnly?), Home, Away, Sets, Players, Actions (IReadOnlyList)
{   public ScoutPlayer? FindPlayer(string fullName); }
public static class DataVolleyFile { public static ScoutMatch Parse(string text); }   // FormatException when not a scout file
public sealed record SkillStats(ScoutSkill Skill, int Total, int Perfect, int Positive, int Ok, int Negative, int Poor, int Error, int? SuccessRate, int? Efficiency);
public static class ScoutStats { public static IReadOnlyList<SkillStats> For(ScoutMatch match, ScoutPlayer player); }
```

`Players` and `Sets` must be lists that compare in order (the tests use `Should().Equal(...)`).

**File format** (DataVolley 2.0 / DV4: sections start with a `[3NAME]` line, fields are separated by `;`):
- Strip a leading BOM, split on `\r\n` or `\n`, and trim the lines' ends.
- `[3MATCH]`: the first line's field 0 is the date, `dd/MM/yyyy`. An empty or invalid date means `null`.
- `[3TEAMS]`: two lines, home first. Field 0 is the code, 1 the name, 2 the sets won (int).
- `[3SET]`: one line per set. Only lines whose field 0 is `True` are played. Field 4 is the final score, `"25-21"` with
  optional spaces.
- `[3PLAYERS-H]` / `[3PLAYERS-V]`: field 1 is the shirt number, 8 the DataVolley id (empty means null), 9 the last name,
  10 the first name.
- `[3SCOUT]`: field 0 of each line is the code. It is an action only when it matches
  `^(?<side>[*a])(?<num>\d{2})(?<skill>[SRABDEF])(?<type>[HMQTUNO])(?<eval>[#+!\-/=])`. Anything after the evaluation
  is ignored (combination, zones, …). Skip everything else: `*p`, `ap` (points), `*z` (rotation), `**` (set marks),
  `$$` (team errors), `*P` / `*C` (substitutions).
- Skills: S Serve, R Reception, A Attack, B Block, D Dig, E Set, F Freeball.
- Missing `[3TEAMS]` (fewer than 2 lines) or a missing `[3SCOUT]` section is a `FormatException("Not a DataVolley scout file.")`.
  Never put the file's text in the exception or a log.

**`FindPlayer(fullName)`:** normalise both sides:
1. decompose with `NormalizationForm.FormD` and drop non-spacing marks;
2. lower-case with the invariant culture;
3. collapse whitespace.

A player matches when the normalised name equals `first + " " + last` or `last + " " + first`. Return the first match
in file order. An empty name returns null, and a partial name never matches.

**Stats** (proposed formulas, pinned by `ScoutStatsTests`):
- six skills in the order Serve, Reception, Attack, Block, Dig, Set; Freeball is never shown;
- only actions with this player's side **and** number count;
- percentages are `(int)Math.Round(100.0 * x / total, MidpointRounding.AwayFromZero)`, or null when total is 0.

| Skill | Success rate | Efficiency |
|---|---|---|
| Serve | # | # − = |
| Reception | # + + | # + + − = |
| Attack | # | # − = − / |
| Block | # | # − = |
| Dig | # + + | # + + − = |
| Set | # | # − = |

### 4.2 The viewer reads — `src/SsSport.Drive.Application/Links/ViewerScouts.cs`

- `ViewerScoutsSpec(playerId)`: ready and visible `AssetKind.Scout` assets, by `SortOrder`. `ViewerScoutSpec(playerId, scoutId)` finds one.
- `GetLinkPlayerScoutsQuery(Code, SessionToken, PlayerId, PageRequest Paging) : IPagedQuery<LinkScoutDto>` answers
  `LinkScoutDto { Id, Title }`, paged in SQL through `IPagedReader.PageAsync`.
- `GetScoutReportQuery(Code, SessionToken, PlayerId, ScoutId) : IQuery<ScoutReportDto>`:
  1. Run the same checks as Phase 1.
  2. Read the original: `storage.GetAsync(asset.OriginalKey)`. Null means `NotFoundException("Scout file")`.
  3. Decode the text: UTF-8 with `throwOnInvalidBytes: true`, falling back to Latin-1 (`Encoding.Latin1`). Old DataVolley files are Windows-1252/Latin-1.
  4. `DataVolleyFile.Parse`. A `FormatException` becomes `DomainException("scout.unreadable", "This scout file could not be read.")` (422).
  5. `match.FindPlayer(player.Name.Value)`, then `ScoutStats.For`.
- **Cache** the parsed `ScoutMatch` in `IMemoryCache` for 10 minutes, keyed by `scout:{assetId}:v{version}` (register
  `AddMemoryCache` if it is not there yet). Do not test the cache with timing.
- DTOs (map with Mapperly, or with small explicit mappers if Mapperly cannot map the records cleanly):

```text
ScoutReportDto   { Guid Id; string Title; ScoutMatchDto Match; ScoutPlayerDto? Player; IReadOnlyList<SkillStatsDto> Skills }
ScoutMatchDto    { DateOnly? Date; string HomeTeam; string AwayTeam; int HomeSets; int AwaySets; IReadOnlyList<SetScoreDto> Sets }
SetScoreDto      { int Home; int Away }
ScoutPlayerDto   { int Number; string Team }            // Team = that side's team name
SkillStatsDto    { string Skill; int Total; int Perfect; int Positive; int Ok; int Negative; int Poor; int Error; int? SuccessRate; int? Efficiency }
```

  - When the player is not found, answer `Player = null` and `Skills = []`.
  - `Date` serialises as `"2026-03-12"`, the default for `DateOnly`.
- **Player stats:** `PlayerStatsDto` gains `Matches` (ready and visible scout files). Count it in `LinkPlayerReader`'s
  single projection (`LinkPlayerFacts` gains `ScoutCount`) and map it. Also update the `ViewerContentDtos` doc comments
  that say "DataVolley files wait for V-8".

### 4.3 API (in `ViewerMediaEndpoints.cs`, same rules as 1.2)

| Route | Name | Answers |
|---|---|---|
| `GET /links/{code}/players/{playerId:guid}/scouts` (`page`, `pageSize`) | `ListScoutFiles` | 200 `PagedResult<LinkScoutDto>`, 400, 401, 404 |
| `GET /links/{code}/players/{playerId:guid}/scouts/{scoutId:guid}` | `GetScoutReport` | 200 `ScoutReportDto`, 401, 404, 422 |

Give the Scalar example the sample match from the test (Club Aurora 3–1 Volley Nord).

**Green:** the `Scouting` unit tests and the V-8 tests in `ViewerMediaEndpointTests` (`Scout_files`, `A_scout_report`,
`A_scout_file_without`, `An_unreadable`, `Only_a_visible_scout`, `The_player_stats_count`). Commit.

---

## Phase 5 — V-8 web: "Match stats" (SSD-101)

- `web/src/lib/viewer/scout.ts`: export the `ScoutReport` type, matching `ScoutReportDto` in camelCase, plus:
  - `formatRate(n)`: `"43%"`; a negative value uses U+2212, `"−100%"`; null gives `"—"`.
  - `formatMatchDate(iso | null)`: `"12 Mar 2026"` (en-GB, UTC), or `""`.
  - `setScores(match)`: `"25–21 · 22–25"` (en dash U+2013, middle dot).
  - `matchResult(report)`: from the player's team side, `"Won 3–1"` or `"Lost 1–3"`. Without a player, the plain home-away score `"3–1"`.
- `web/src/components/viewer/scout/copy.ts`. Exact strings, all used by the tests:
  - `section: "Match stats"`, `dialogLabel: (t) => \`${t}, match stats\``, `close: "Close match stats"`;
  - `tableLabel: (name) => \`${name}'s stats per skill\``;
  - the column headers `Skill`, `Total`, `Perfect`, `Errors`, `Success`, `Efficiency`;
  - `notInFile: (name) => \`${name}'s actions are not in this scout file.\``;
  - `unreadable: "This scout file could not be read."`;
  - `playerLine: (n, team) => \`#${n} · ${team}\``.
- `scout-report.tsx`, `ScoutReportView({ report, playerName, viewerEmail?, onClose })`:
  - a `role="dialog"` with `aria-modal` and `aria-label = dialogLabel(title)`;
  - `<h2>` with the title;
  - the home and away team names, `matchResult`, the date, `setScores` and the player line;
  - a `<table aria-label=tableLabel(playerName)>` with a header row and one **`<td>`** row per skill (the skill name is
    a `<td>`, not a `<th>`: the tests read every column with `getAllByRole("cell")`); the cells show
    Total, Perfect, Errors, `formatRate(successRate)`, `formatRate(efficiency)`;
  - without a player, the `notInFile` text instead of the table;
  - behaviour as `DocumentViewer`: focus on Close, Escape and Close call `onClose`, body scroll locked, and
    `onContextMenu` prevented on the whole dialog;
  - `EmailOverlay` from `player/email-overlay` over the content when `viewerEmail` is given;
  - no download anywhere, and never the word "download".
- `scouts-section.tsx`, `ScoutsSection({ scouts, playerName, viewerEmail? })`:
  - nothing at all when the list is empty;
  - otherwise a section with heading "Match stats" and one button per file (its title);
  - a click calls `fetchScoutReport(reportPath)` and opens `ScoutReportView`;
  - on failure, a `role="alert"` with `unreadable`;
  - return focus to the button when the dialog closes.
- Put `ScoutsSection` on the player page after Documents (`player-page.tsx`). Add `stats.matches` to the stats row only
  when it is above 0 (label "Matches").
- **Design:** the canvas has no artboard for this yet. Build it with the existing tokens and the `DocumentViewer`
  dialog's look (`dv-*` styles as the model; new styles in a `scout.css` imported like the other viewer CSS). Add a
  design preview route `web/src/app/(viewer)/design/scout/page.tsx` with the sample report, so the owner can review it.
  Say in the PR that the owner should approve it or ask for a canvas artboard.

**Green:** `cd web && npx vitest run` (everything), `npm run typecheck`, `npm run lint`. Commit. Close **V-8** in Jira.
**Epic 4 is then done:** set the README roadmap row 4 to ✅ Done.

---

## Phase 6 — L-7 first-open alert (SSD-63)

### 6.1 Domain

- `src/SsSport.Drive.Domain/Links/LinkOpen.cs`:
  - `public sealed record LinkOpened(Guid LinkOpenId) : IDomainEvent;`
  - `public sealed class LinkOpen : AggregateRoot<Guid>` with `ShareLinkId`, `Email`, `IsForward`, `FirstOpenedAt`;
  - `static Record(Guid shareLinkId, EmailAddress email, bool isForward, DateTimeOffset now)`: id
    `Guid.CreateVersion7(now)`, `ArgumentNullException.ThrowIfNull(email)`, raises `LinkOpened(id)`.
- `NoticeKind.LinkOpened` (append to the enum; it is stored as a string or int, so check `NoticeBoxConfiguration`, and
  add a migration if needed).

### 6.2 Persistence

- In `ViewerGateConfiguration.cs`, add a `LinkOpenConfiguration` for table `link_opens`: `id`, `share_link_id` (FK to
  `share_links`, cascade), `email` (same converter as `link_forwards.email`), `is_forward`, `first_opened_at`. Add an
  index on `(share_link_id, email)`.
- **Not unique (proposed):** two first sessions racing must never fail the viewer's sign-in. The worst case is two alerts.
- Never purged: L-2b's `gate-purge` must not touch it. Add a line to its XML doc saying so.
- Migration `AddLinkOpens` (`dotnet ef migrations add AddLinkOpens -p src/SsSport.Drive.Infrastructure -s src/SsSport.Drive.Api`).

### 6.3 Recording the open

- `ViewerGateRules.StartSession` becomes `StartSessionAsync(sessions, opens, secrets, link, email, isForward, deviceId, now, ct)`.
  After adding the session, when `opens.AnyAsync(new LinkOpenSpec(link.Id, email))` is false, it adds `LinkOpen.Record(...)`.
- Update all three callers: verify, open with a device, and a link that skips the gate.
- The unit of work saves both in one transaction, and the event goes to the background queue as for `LinkForwarded`.

### 6.4 The alert — `src/SsSport.Drive.Application/Telegram/Links/LinkOpenAlerts.cs`

- `NotifyAdminsOfLinkOpen : IBackgroundEventHandler<LinkOpened>`. It loads the open and the link, and returns when either
  is gone or `!link.NotifyOnOpen`.
- Otherwise, for each `ActiveAdminsSpec` user, call
  `outbox.QueueAsync(admin, NoticeKind.LinkOpened, playerId: null, text)`. That goes through the T-8 rules, unlike
  forwards.
- `BotTexts.resx` key `LinkOpenedAlert`: `👀 Link {0} for {1} was opened by {2} · {3}`, with
  {0} the code, {1} the recipient's name, {2} the email, {3} the player names joined by ", ".
  - For a forward, append ` (forwarded)` with key `LinkOpenedForwardSuffix`.
  - Add `BotTexts.FormatLinkOpenedAlert(...)` like the other `Format*` helpers.
- Log only the link id (`[LoggerMessage]` at Information: "Admins told that link {ShareLinkId} was opened"), never the email.

**Green:** `LinkOpenTests` and the first three tests of `AdminAlertsTests`. Also re-run `ViewerGateEndpointTests` and
`GatePurgeTests`: they must stay green. Commit.

---

## Phase 7 — L-7 daily digest (SSD-63)

### 7.1 Application — `src/SsSport.Drive.Application/Telegram/Digest/DailyDigest.cs`

```csharp
public sealed record DigestWindow(DateOnly Day, DateTimeOffset Start, DateTimeOffset End)
{   public static DigestWindow Yesterday(DateTimeOffset now, TimeZoneInfo zone); }   // local date of now − 1 day, [00:00, 24:00) local, as UTC instants
public sealed record DigestTopPlayer(string Name, int WatchMinutes);
public sealed record DigestFacts
{
    public DateOnly Day { get; init; }
    public int LinksOpened { get; init; }  public int Viewers { get; init; }  public int Plays { get; init; }  public int WatchMinutes { get; init; }
    public IReadOnlyList<DigestTopPlayer> TopPlayers { get; init; } = [];
    public int PendingForwards { get; init; }  public int WaitingPlayers { get; init; }  public int? OpenAccessRequests { get; init; }  public int FailedEmails { get; init; }
    public bool HasActivity => LinksOpened > 0 || Viewers > 0 || Plays > 0 || WatchMinutes > 0;
}
public interface IDigestFactsReader { Task<DigestFacts> ReadAsync(DigestWindow window, CancellationToken cancellationToken); }
public static class DailyDigestText { public static string Render(DigestFacts facts); }
public sealed record SendDailyDigestCommand(DateTimeOffset Now) : ICommand<int>;     // returns how many admins got it
```

- `Render` joins the lines below with `\n`. Every string comes from `BotTexts.resx`, never inline. Proposed wording, pinned by `DailyDigestTests`:
  1. `📊 Daily digest · {day:ddd d MMM yyyy}` (invariant culture);
  2. with activity: `Yesterday: {n} link(s) opened · {n} viewer(s) · {n} play(s) · {n} min watched`; without: `Yesterday: no activity.`;
  3. only when `TopPlayers` is not empty: `Top players: ` followed by at most the first 3 entries, each `{name} {m} min`, joined by ` · `;
  4. the waiting parts that are above 0, in order:
     - `{n} forward(s) to keep or block`
     - `{n} player(s) on the waiting list`
     - `{n} access request(s)` (only when `OpenAccessRequests` is not null)

     joined by ` · ` after `Waiting on you: `. With no part, the line is `Nothing is waiting on you.`;
  5. only when `FailedEmails > 0`: `⚠️ {n} email(s) failed after every retry`.

  Singular is used for exactly 1. Put the singular and plural forms as separate resx keys.
- `SendDailyDigestCommandHandler(IDigestFactsReader, IRepository<User, Guid>, ITelegramMessenger, INotificationSettings)`:
  - the window is `DigestWindow.Yesterday(request.Now, settings.Rules.TimeZone)`, then read the facts and render;
  - send to each `ActiveAdminsSpec` user with `bot.SendAsync(chatId, text, keyboard: null)`, at once, **not** through
    the outbox;
  - one admin's Telegram failure is logged (Warning, user id only) and does not stop the others;
  - return the count sent.

### 7.2 Infrastructure

- `src/SsSport.Drive.Infrastructure/Persistence/DigestFactsReader.cs : IDigestFactsReader`, next to
  `LinkPlayerReader`. Use `AsNoTracking`, SQL counts only, never load rows:
  - `LinksOpened` = distinct `share_link_id` of `viewer_sessions` with `created_at` in `[Start, End)`;
  - `Viewers` = distinct `email` of the same sessions;
  - `PendingForwards` = `link_forwards` with `Status == Pending` (all, not windowed);
  - `WaitingPlayers` = players with `Status == Waiting`;
  - `OpenAccessRequests` = `null` until P-4;
  - **7a:** `Plays`, `WatchMinutes`, `TopPlayers` and `FailedEmails` = 0 / empty, with a `// L-7 7b` comment.
- `DailyDigestJob`, the same shape as `GatePurgeJob`:
  - `RecurringJobId = "daily-digest"`, queue `BotNoticeJob.Queue`, `[AutomaticRetry(Attempts = 0)]`, `[DisableConcurrentExecution(600)]`;
  - it sends `new SendDailyDigestCommand(timeProvider.GetUtcNow())`;
  - register it in `RegisterRecurringJobs` with `Cron.Daily(5, 30)`: 05:30 UTC = 09:00 Tehran, with no daylight saving
    since 2022. Say so in a comment and in the method's XML doc.
- DI: `services.AddScoped<IDigestFactsReader, DigestFactsReader>()` and `AddTransient<DailyDigestJob>()`, as for the
  other jobs.

**Green (7a):** `DailyDigestTests` and the digest tests of `AdminAlertsTests`. Commit.

### 7b — after `feature/backend-tracking-email-signin` is merged into `main`

1. Rebase onto `main`. Re-run everything.
2. Fill in the reader from the merged tables: `Plays`, `WatchMinutes` (rounded total) and the top three players by
   watch minutes (ties by name) from L-5/L-6's tracking, all in `[Start, End)`. `FailedEmails` = `OutgoingEmail` with
   `Status == Failed` and `LastAttemptAt` in the window.
3. **Write a test first** in `AdminAlertsTests`: seed watch data and one failed email in the window, plus one outside it,
   and expect the full digest lines. Watch it fail, then make it pass.
4. Close **L-7** in Jira. Once L-4/L-5/L-6 are also Done, set README roadmap row 5 to ✅ Done.

---

## Phase 8 — housekeeping and PR

- **Scalar:** every new endpoint has a name, summary, description, tag, schemas, all ProblemDetails, an example and its
  auth scheme. Check `/scalar` locally.
- **`.claude/backlog.md`:**
  - mark V-5b and V-8 done with the date and the PR;
  - L-7 as done or "7b pending";
  - add **I-9** (new task): "I-3/I-4 write HLS and page images to the V-5b folders (`MediaFolders`)". Make it a Jira
    task under Epic 3 and record the key in `.claude/jira-keys.md`.
- **README:** roadmap rows 4 and 5, the edge section (pages route, short-link pass-through), and the API list.
- **`.gitignore` / `.dockerignore`:** nothing new is expected; check.
- **decisions.md:** if the owner changes any (proposed) value in review, record it the same day.
- **PR** into `main`, titled "Signed media, DataVolley stats and admin digest (V-5b, V-8, L-7)". The description lists:
  - the (proposed) values (folders, stats formulas, digest wording, the non-unique `link_opens` index);
  - the design preview route `/design/scout`;
  - the 7b status;
  - the test results.
- **Remind the owner about k6** (CLAUDE.md step 9): the stream and pages endpoints belong in `perf/k6/viewer-links.js`
  (K-2: player page + token refresh), and the scout report read too. Offer to write the scenarios.
