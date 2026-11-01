# SS-SPORT Drive

Branded, trackable volleyball player video pages built from Google Drive folders, for the SS-SPORT agency.

**Read first:** [decisions.md](decisions.md) is the owner's full decision log and overrides the brief and the spec where they differ. The backlog is in [backlog.md](backlog.md) (Jira import: [jira-import.csv](jira-import.csv)).

## Backlog & Jira (always)
- **Read [backlog.md](backlog.md) at the start of every session.** We work strictly from it, in epic/phase order, unless the owner says otherwise.
- **Jira** is the live tracker: site `bonyadin2000.atlassian.net`, project **SSD** ("SS-SPORT Drive"). The Jira key for each backlog key is recorded in [jira-keys.md](jira-keys.md).
- **Keep Jira in sync with the work:**
  - When you start a task, move it to *In Progress*.
  - When it is implemented, tests are green and it is committed, comment with what was done (branch, commit, test result) and move it to *Done*.
  - New work discovered along the way becomes a new Jira task, and is added to backlog.md too.

## Per-feature workflow (always, in this order)
1. **Explain and ask.** Describe the feature to the owner (in Persian) and wait for approval.
2. **Branch.** Create a branch for the feature or page (`feature/<area>-<short-name>`, e.g. `feature/viewer-player-page`). Never work on `main`.
3. **Tests first (TDD).** Before writing any controller, endpoint, MediatR handler, domain rule or service, write its tests (unit + integration where relevant).
4. **Confirm the tests fail.** Run them and see them fail for the right reason.
5. **Implement.** Write the code to make them pass.
6. **Verify green.** Run the full test suite and report the real result. Never claim tests pass without running them, and never commit with failing tests.
7. **Housekeeping.** Before committing, check every point in "Keep up to date" below.
8. **Commit.** When a feature is implemented and green, commit it on its branch with a clear English message. Open a PR into `main`; the CI pipeline must pass.
9. **Remind about benchmarks.** After finishing any viewer page, API endpoint or other user-facing area, remind the owner to run a **k6** benchmark for it, and offer to write or update the k6 script.

## Code rules
- **MediatR** for every command and query.
- **Architecture:** CQRS + DDD + Clean Code + SOLID + DRY.
  - **Unit of Work** plus a generic `IRepository<T>` for writes.
  - Query conditions are expressed with the **Specification pattern** (Ardalis.Specification style). Never scatter ad-hoc `Where` chains through handlers.
  - Read-side queries use direct `AsNoTracking` projections.
  - **DTO pattern:** request/response DTOs for every endpoint, mapped with **Mapperly** (compile-time; AutoMapper was dropped for a security advisory). Entities are never exposed outside the Application layer.
  - **Pagination (owner, 2026-10-25):** never send a whole table to the front-end, the bot or the user. Every list that can grow is paged on the server, in SQL, through the specification; nothing is loaded whole and paged in memory. Full rules in [decisions.md](decisions.md) §2b.
- **XML doc comments on every .NET method, class, interface and property** (public and private). TSDoc/JSDoc on every exported TypeScript function and component.
- **English only** in code, comments, logs and commits. No Persian text in code. UI copy goes in resource files.
- **Logging:**
  - Serilog with structured, professional logging: message templates with no string concatenation; correlation/request IDs, user and player IDs as properties; levels used correctly.
  - No secrets, tokens, OTP codes or full emails are ever logged.
  - Logs ship to **Grafana Loki** and are viewed in **Grafana** dashboards.

## Security rules (owner, 2026-11-01; always)
Every change is checked against these before it is committed. Full text in [decisions.md](decisions.md) §1b.
- **No secrets in code.** Keys, tokens, passwords and connection strings come only from environment variables or secret stores. Never in commits, logs, images or front-end bundles.
- **Database locked down:** never publicly reachable, least-privilege app role, TLS in production, encrypted backups.
- **Server-side validation** of every input (FluentValidation in the MediatR pipeline). Client checks are UX only.
- **XSS:** encode all output, no raw HTML from user data, strict CSP.
- **Admin area locked:** admin pages and APIs require the Admin role policy on the server, deny by default.
- **HTTPS forced:** HTTPS redirection and HSTS in production.
- **Hash with salt:** passwords (if ever added) with a slow salted hash; OTP codes hashed with salt; magic-link and session tokens stored only as keyed hashes. Compare in constant time.
- **Log suspicious activity** (failed sign-ins/OTPs, bad secrets, rate-limit hits, CSRF failures, admin denials) as structured warnings, with no secrets.
- **Parameterized queries only.** Never build SQL by string concatenation.
- **Rate limiting** on auth, OTP, magic-link, access-request, webhook and public endpoints.
- **CORS:** explicit origin allow-list only.
- **Security headers** on the API and the web app: CSP, HSTS, X-Content-Type-Options, frame-ancestors, Referrer-Policy, Permissions-Policy.
- **Session cookies:** `HttpOnly`, `Secure`, `SameSite`, `__Host-` prefix, short lifetime.
- **CSRF protection** on every cookie-authenticated state-changing request.

## API documentation (Scalar)
- Every API is documented with an **OpenAPI** document (`Microsoft.AspNetCore.OpenApi`) and rendered with **Scalar** (`Scalar.AspNetCore`) at `/scalar`.
- Every endpoint must carry:
  - a name, summary and description;
  - tags grouped by area;
  - request and response schemas, including all error responses (ProblemDetails);
  - realistic examples;
  - its auth requirements.
- XML doc comments are fed into the OpenAPI document, so they show up in Scalar.
- Scalar is public in local and staging. In production it sits behind admin auth.
- A new or changed endpoint is not done until its Scalar docs are complete and accurate.

## Keep up to date (check on every commit)
- `.gitignore`: covers every new tool, build output, secret or local file introduced.
- `.dockerignore`: every Docker context excludes tests output, `node_modules`, `bin/obj`, `.git`, secrets and local env files.
- `README.md`: always present and polished (centered header, badges, architecture diagram, stack, quick start, structure, conventions, roadmap). Update it whenever a feature changes setup, structure, stack or status.
- `LICENSE`: always present. The project is **proprietary, all rights reserved (SS-SPORT)**; never add an open-source license.
- `.claude/decisions.md`: any new owner decision is recorded the same day.
- XML docs present on every new method.
- Scalar/OpenAPI docs complete for every new or changed endpoint.

## CI/CD & DevOps
- A complete pipeline (GitHub Actions unless the owner chooses otherwise). Every PR runs build, lint, unit tests, integration tests (Testcontainers) and Docker image builds.
- `main` deploys to staging automatically. Production deploys from a tagged release with a manual approval.
- Everything runs in Docker. Configuration comes from environment variables and secrets, never from committed files. Health checks, migrations as a pipeline step, and documented rollback.
- k6 smoke tests run in CI against staging. Full k6 load tests are run on demand.

## Other
- Ask before building anything still listed as open in decisions.md §10.
