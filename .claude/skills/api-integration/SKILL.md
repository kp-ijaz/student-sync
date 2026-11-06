---
name: api-integration
description: Use when adding or changing any Student Sync API endpoint, DTO/contract, validation, error response, pagination, idempotent command, or the Blazor/Flutter client calls that consume them.
---

# API integration

> **Flutter is parked this phase (owner decision 2026-11-04).** Student Sync currently ships
> the web portals and the installable PWA only. Treat every Flutter/mobile-app instruction in
> this skill as suspended: do not build, test, mirror or verify anything under
> `mobile/student_sync` until the owner re-enables the app.

## Purpose and triggers
Every feature crosses the API: Blazor Web and Flutter are thin clients over one ASP.NET Core minimal-API backend.
Use this skill for new endpoints, contract changes, client wiring, error handling, retries, pagination and
idempotency, together with `student-sync-security` for authorization.

## Inspect first
- `src/StudentSync.Api/Endpoints/CoreEndpoints.cs` (auth, me, platform, institution, people, academics, sessions,
  dashboard, reports) and `LearningEndpoints.cs` (notes, materials, recordings, videos, assignments, exams,
  attempts, questions, doubts, ai, files, library, my/subjects). Groups: `api.MapGroup("/x").WithTags(...)
  .RequirePermission(...)`; handlers delegate to Application services and return `Results.*`.
- `src/StudentSync.Api/Hosting/Plumbing.cs` — API versioning (`/api/v1`), current user, `Idempotency-Key`
  filter (stored per user+key, replayed); `ErrorHandling.cs` — RFC 7807 `ProblemDetails` with a stable `code`
  (`validation_failed` + `errors` dictionary, not found, forbidden, domain rule, conflict).
- `src/StudentSync.Contracts/*.cs` — request/response records shared with the Web client (`Common.cs` has
  paging/lookup types). Flutter mirrors them in `mobile/student_sync/lib/core/models/`.
- `src/StudentSync.Application/Common/Abstractions.cs` — `IAppDbContext`, `ICurrentUser`; `Common/Errors.cs` — exceptions
  (`RequestValidationException`, `NotFoundException`, `DomainException`, …).
- `src/StudentSync.Web/Services/ApiClient.cs` — typed HTTP client (token handling, problem parsing) and
  `UiServices.cs` (menus, API URL config); `mobile/student_sync/lib/core/` for the Flutter client/auth refresh.
- `tests/StudentSync.IntegrationTests/ApiFixture.cs` — `LoginAsync`, `Get<T>`, `Post<T>`, `PostRaw`, `Ensure`.
- Swagger at `http://localhost:5080/swagger` when the API runs.

## Rules
- Thin endpoints: validation + authorization + service call; business rules in Application/Domain.
- Explicit request records; never bind entities. Validate on the server and return `validation_failed` with
  field errors; clients show them next to the field.
- Consistent responses: 200/201 with a DTO, 204 for commands, 202 for accepted background work; problem details
  with `code` for errors; lists are paged (`page`, `pageSize` capped) or bounded (`Take(n)`).
- Every state-changing endpoint that a client may retry (submit, complete session, payment callback, hour
  adjustment) accepts `Idempotency-Key`; clients send a fresh GUID per user action and reuse it on retry.
- Tenant scoping and `RequirePermission` on every group/endpoint; ids in the route are verified against the
  caller's scope inside the service.
- Times in DTOs are UTC `DateTime` plus the relevant `timeZoneId`; clients convert for display (`LocalTime`
  component on web, IANA handling in Flutter).
- File access only through short-lived links from the files endpoints; DTOs carry a `fileId`, never a storage key.
- Contract changes are backwards-compatible where Flutter is already shipped (add fields, do not rename); update
  Contracts, ApiClient usage, Flutter models and tests together and list them in the report.
- Background work goes through `IBackgroundJobs` (Hangfire); endpoints return quickly with a status to poll.

## Workflow
1. Define the request/response records in `src/StudentSync.Contracts` and the service method signature.
2. Implement the service with scoped queries, validation, audit, transaction/idempotency as needed.
3. Map the endpoint in the right group with `RequirePermission`; add tags/names for Swagger.
4. Integration tests: success, validation 400 with field errors, 403 wrong role, 404 other institution/other
   student, replay with the same `Idempotency-Key`, paging bounds.
5. Wire the Web client through `ApiClient` and show loading/empty/error/forbidden via the shared state components;
   wire Flutter if the feature is in the mobile scope.
6. Smoke the live API with `curl` as a demo user when useful; check Swagger renders the new contract.

## Acceptance criteria
- Endpoint behaves per the conventions above with tests proving each status path.
- Web (and Flutter where applicable) consume the real endpoint; no mocked or half-wired primary action.
- Error cases surface as clear UI messages, not blank screens or console-only failures.

## Completion checklist (report exactly)
- Endpoints added/changed (method + route + permission), contracts changed, clients updated.
- Integration tests added and results; curl/Swagger checks done.
- Compatibility notes for Flutter and any deferred client work.
- Related: `student-sync-security`, `student-sync-testing`, `puppeteer-browser-testing`.
