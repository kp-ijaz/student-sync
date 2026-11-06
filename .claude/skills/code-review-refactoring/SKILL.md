---
name: code-review-refactoring
description: Use before declaring any substantial Student Sync change complete — review the full diff for correctness, security, regressions, duplication, dead code and scope creep, and refactor only where tests protect the change.
---

# Code review and refactoring

> **Flutter is parked this phase (owner decision 2026-11-04).** Student Sync currently ships
> the web portals and the installable PWA only. Treat every Flutter/mobile-app instruction in
> this skill as suspended: do not build, test, mirror or verify anything under
> `mobile/student_sync` until the owner re-enables the app.

## Purpose and triggers
Run this skill as the last step of every substantial task (CLAUDE.md §11 step 8, §13) and whenever asked to review
a diff, a PR or a module. It is a correctness-and-maintainability pass; security specifics are in
`student-sync-security` and tests in `student-sync-testing`.

## Inspect first
- `git status` and `git diff` (the repo has no commits yet; review the working tree against the task scope).
- `ARCHITECTURE.md` — module boundaries (Domain → Application → Infrastructure/Api/Web; Contracts shared with Web
  and Flutter), conventions for services, DTOs and endpoints.
- The module's existing service in `src/StudentSync.Application/<Module>/` and its endpoint group, to spot
  duplicated rules or helpers already available (`StudentViews.cs`, `TeachingRules.cs` in Web Services; domain
  rule classes such as `StudentCurriculum`).
- `src/StudentSync.Infrastructure/Persistence/ModelConfiguration.cs` and the newest migration for schema changes.
- `DESIGN.md` and `src/StudentSync.Web/Components/Shared/` for UI reuse.

## Review checklist
Correctness
- Domain rules live in Domain/Application, not in Razor components or endpoints; Web and Flutter share the same API.
- Nullable `BatchId`/`ArrangementId` paths handled; one-to-one and ad hoc sessions never need a batch.
- UTC storage, IANA zone kept, DST-safe conversions; no `DateTime.Now`; `IClock` used.
- Errors are deliberate: no swallowed exceptions, no success responses for failed operations; `CancellationToken`
  propagated.
- Concurrency: `RowVersion` on mutable records, transactions around multi-record writes, idempotency on retries.
- Migrations: reviewed by hand, additive, data-preserving, no cascade deletes of academic/financial history,
  indexes match real queries, uniqueness enforced in the database.
Security (summary; full list in `student-sync-security`)
- `RequirePermission`, tenant scoping in queries, no client-supplied ids trusted, audit present, no secrets.
Quality
- No copy-pasted logic between services or screens; reuse shared components and services.
- No speculative abstractions, god services or unrelated refactors; names clear; comments explain why, not what.
- Loading/empty/error/forbidden states present for new UI; accessible labels; responsive at 375/768/1440.
- Tests added for new rules and endpoints; old tests still pass; `dotnet format` clean.
- Docs updated: `IMPLEMENTATION_STATUS.md`, `DESIGN.md` (UI), `ARCHITECTURE.md` (new modules/entities),
  `DEVELOPMENT.md` (new commands), the relevant `.claude/skills/*/SKILL.md`.
Scope
- Nothing outside the task changed; nothing working removed without owner approval; no secrets or build output
  in the diff (`bin/`, `obj/`, `App_Data/`, `.env`, screenshots with real data).

## Refactoring rules
- Refactor only with tests covering the behaviour; keep refactors in separate, small steps from feature work.
- Prefer extracting a shared rule/component when the third duplicate appears, not on speculation.
- Keep public API contracts stable for Flutter; if a DTO changes, update `src/StudentSync.Contracts`, the Web
  client and `mobile/student_sync/lib/core/models` together and say so.

## Workflow
1. Read the whole diff file by file; note findings by severity (blocker, should-fix, nit).
2. Verify each blocker with a test or a reproduction, not by assumption.
3. Fix blockers and should-fixes in the same change; list nits left behind.
4. Re-run build, format, unit, integration and relevant browser checks.

## Completion checklist (report exactly)
- Findings fixed vs deferred (with reasons); files touched.
- Commands re-run after fixes and their results.
- Related: `student-sync-security`, `student-sync-testing`, `api-integration`.
