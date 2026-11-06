---
name: student-sync-testing
description: Use before, during and after any meaningful change — defines the Student Sync test strategy (unit, integration against SQL Server, API/authorization, Flutter, browser, regression) and how to run and report each suite honestly.
---

# Student Sync testing

> **Flutter is parked this phase (owner decision 2026-11-04).** Student Sync currently ships
> the web portals and the installable PWA only. Treat every Flutter/mobile-app instruction in
> this skill as suspended: do not build, test, mirror or verify anything under
> `mobile/student_sync` until the owner re-enables the app.

## Purpose and triggers
Testing is part of implementation (CLAUDE.md §8, §13). Use this skill for every change that touches domain rules,
application services, endpoints, persistence, UI, or the Flutter app, and before reporting any task complete.
"Tested" means a test was run and its result observed, never that code was reviewed.

## Inspect first
- `tests/StudentSync.UnitTests/` — `DomainTests.cs`, `TeachingRulesTests.cs`, `StudentViewsTests.cs`,
  `ImageMetadataStripperTests.cs` (xUnit, no database; domain/application rules and pure functions).
- `tests/StudentSync.IntegrationTests/` — `ApiFixture.cs` (boots the API in-process, starts SQL Server 2022 through
  Testcontainers, runs migrations and the demo seeder with `Accounts.Password`, exposes `LoginAsync`, `Get/Post/Put`
  helpers), `ApiTests.cs`, `CurriculumTests.cs`, `MySubjectsTests.cs`, `ProfilePhotoTests.cs`.
- `tests/e2e/` — Playwright journeys (`tests/e2e/tests/journeys.spec.ts`), visual baselines (`tests/e2e/tests/visual.spec.ts`), axe
  scans; `puppeteer/` for ad hoc browser checks (see `puppeteer-browser-testing`).
- `mobile/student_sync/test/` — Flutter widget/unit tests; `scripts/capture_screenshots.sh` for simulator runs.
- `IMPLEMENTATION_STATUS.md` — last recorded counts and known gaps; `DEVELOPMENT.md` — commands; `.github/` — CI.
- `src/StudentSync.Infrastructure/Seeding/` — demo data every test relies on (idempotent, fictional).

## Rules
- Record the baseline (build + suites) before changing code; report pre-existing failures separately.
- Every domain/application rule gets a unit test; every endpoint change gets an integration test covering the
  happy path, validation failure, wrong role (403) and other institution/other student (404 or 403 per existing
  convention). Tenant isolation and student data isolation tests are mandatory for new data.
- Idempotent commands (`Idempotency-Key` header, see `src/StudentSync.Api/Hosting/Plumbing.cs`) get a replay test.
- Time-zone/recurrence logic gets DST and date-boundary cases; money/hour logic gets rounding, double-submit and
  concurrency cases (RowVersion conflicts).
- Seeders must be run twice in a test to prove no duplicates.
- Tests use fictional data only (`example.test` domains) and never real credentials.
- Integration tests need Docker running (SQL Server image already present); do not skip them silently.

## Commands
```bash
dotnet build StudentSync.slnx
dotnet test --project tests/StudentSync.UnitTests
dotnet test --project tests/StudentSync.IntegrationTests        # Docker + Testcontainers SQL Server 2022
dotnet format StudentSync.slnx --verify-no-changes --severity error
cd tests/e2e && npx playwright test                              # stack running on :5080/:5090, demo seeded
cd tests/e2e && npm run puppeteer:smoke                          # or node puppeteer/<flow>.mjs
# cd mobile/student_sync && flutter analyze && flutter test   # Flutter parked this phase — do not run
```
Filter one test: `dotnet test --project tests/StudentSync.IntegrationTests --filter "FullyQualifiedName~Curriculum"`.

## Workflow
1. Run the relevant suites first and note the baseline counts.
2. Write the failing test for the new rule or endpoint (unit for pure logic; integration for anything crossing
   the API, EF Core, authorization or the database).
3. Implement; keep the test green; add the negative cases (validation, 403, cross-tenant, cross-student, replay).
4. For schema changes: new migration reviewed, applied by the fixture (empty DB) and locally on the dev DB
   (`dotnet run --project src/StudentSync.Api -- migrate`), seeder run twice.
5. Run build, format, unit, integration; then browser checks for UI changes; (`flutter test` only once the Flutter app is re-enabled).
6. Update `IMPLEMENTATION_STATUS.md` counts and verification notes.

## Acceptance criteria
- New behaviour is covered by tests that were run and passed; prior tests still pass (or failures are explained).
- Authorization and isolation cases are tested for every new endpoint or query.
- Browser verification done for every visible change (`puppeteer-browser-testing`).

## Completion checklist (report exactly)
- Per suite: **run / passed / failed / not run** with counts and the reason for anything not run.
- Tests added or changed (file names) and what they prove.
- Any flaky or environment-dependent test and how it was handled.
- Related: `puppeteer-browser-testing`, `student-sync-security`, `api-integration`, `code-review-refactoring`.
