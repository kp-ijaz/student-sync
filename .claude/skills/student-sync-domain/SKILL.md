---
name: student-sync-domain
description: Use before any domain/business-workflow change in Student Sync — the institute operating model, terminology, existing entities, invariants, and where each concept lives in the repo.
---

# Student Sync domain

> **Flutter is parked this phase (owner decision 2026-11-04).** Student Sync currently ships
> the web portals and the installable PWA only. Treat every Flutter/mobile-app instruction in
> this skill as suspended: do not build, test, mirror or verify anything under
> `mobile/student_sync` until the owner re-enables the app.

## Purpose and triggers

Read this skill first for any task that touches a business rule, entity, relationship or
role behaviour. It maps the institute's operating model (CLAUDE.md addendum sections A–N;
`Student_Sync_Institute_Workflow_Master_Prompt.md` §1–§3) onto the code that already exists.
The other domain skills assume you have read this one.

Triggers: new entity/migration, new API endpoint touching students/teachers/sessions/content,
any question of "who may do what", any workflow from the student journey (master prompt §2).

## Inspect first

- `CLAUDE.md` sections 5 (invariants) and A–O (addendum). `Student_Sync_Institute_Workflow_Master_Prompt.md`.
- `ARCHITECTURE.md`, `IMPLEMENTATION_STATUS.md` (what is built and what was verified).
- Domain: `src/StudentSync.Domain/People/People.cs`, `Academics/Academics.cs`,
  `Academics/StudentCurriculum.cs`, `Scheduling/Sessions.cs`, `Content/Content.cs`,
  `Assessments/Assessments.cs`, `Assignments/Assignments.cs`, `Identity/Identity.cs`
  (`Permissions`, `RolePermissions`), `Common/Entities.cs` (`TenantEntity`, `RowVersion`).
- Application: `src/StudentSync.Application/Common/Abstractions.cs` (`IAppDbContext`,
  `ICurrentUser`, `ITenantContext`, `IFileStorage`, `IBackgroundJobs`, `ILanguageModel`),
  `Common/AccessPolicy.cs` (teacher ↔ student visibility), `Common/AuditAndNotify.cs`.
- Persistence: `src/StudentSync.Infrastructure/Persistence/ModelConfiguration.cs`
  (indexes, FKs, delete behaviour), `Persistence/Migrations/`.
- Seeding: `src/StudentSync.Infrastructure/Seeding/DemoDataSeeder.cs`, `DemoDataSeeder.Curriculum.cs`.
- API: `src/StudentSync.Api/Endpoints/CoreEndpoints.cs`, `LearningEndpoints.cs`.

## Vocabulary (use these words consistently)

| Term | Meaning | In code today |
|---|---|---|
| Institute | The single tenant | `Tenancy/Tenancy.cs`; `TenantEntity.TenantId` on every row |
| Admin / Teacher / Student | The only sign-in roles | `UserRole`; Parent and SuperAdmin exist but are switched off/deferred |
| Grade / class | School class, e.g. Class 10 | `GradeLevel` |
| Syllabus / curriculum | NCERT, Kerala State, … | **Missing** — no entity; `Course` + `Subject.Kind` carry NCERT subject structure only |
| Subject, Chapter, Topic | Catalogue | `Subject`, `Chapter`, `Topic`; per-student subjects via `StudentSubject` + `StudentCurriculum.SubjectsOf` |
| Weak subject / learning need | Self-, parent- or test-identified | **Missing** |
| Diagnostic assessment | Initial basic test | **Missing** as a concept; `Question`/`Exam`/`ExamAttempt` exist and should be reused |
| Learning-hour package | Prepaid hours usable across subjects | **Missing** (no package, ledger or payment entity; `SubscriptionPlan` is the SaaS plan, not this) |
| Teacher assignment | Student ↔ teacher ↔ subject | Partial: `TeacherSubject` (teacher capability), `TeachingArrangement` (1:1/private group), `Batch.TeacherId`; **no** per-subject assignment with request/approval/history |
| Session | One class occurrence, any teaching mode | `LearningSession` (+ `SessionSeries`, `SessionParticipant`); `BatchId` nullable |
| Attendance | Per participant per session | `AttendanceRecord`, unique `(SessionId, StudentId)` |
| Class report | Post-class record | **Missing**; `SessionNote` (Markdown, draft/publish), `LearningMaterial`, `PrerecordedVideo` exist |
| Book / resource | Student-supplied PDF/PPT/DOC | `StoredFile` + `LearningMaterial`; **no** processing pipeline or extracted chapters |
| Student 360 | Teacher's full view of a student | Partial: `StudentProfileAsync`, `ProgressService`, `TeacherStudents.razor` |

## Invariants (never break)

- One institute; keep `TenantId` scoping and global query filters. No new multi-tenant or super-admin features (addendum L).
- Students are **admin-created only**; no `/register`, no `PendingApproval` (addendum B). Account states are admin-controlled.
- Hours, not courses, are sold; money and hours are separate records; hours are consumed only by completed billable sessions, exactly once (addendum A).
- A session never requires a batch; participation is explicit (`SessionParticipant`).
- Teachers see only students they teach (`AccessPolicy.StudentsTaughtBy`) — extend this when adding the assignment model, do not bypass it.
- Server-side authorization via `Permissions` + `AccessPolicy.Require`; hiding UI is not authorization.
- UTC instants + IANA `TimeZoneId`; audit via `AuditService.Record`; optimistic concurrency via `RowVersion`.
- AI output (chapter extraction, drafts, marking help) is advisory until a teacher reviews it.

## Workflow for a domain change

1. Find the existing entity/service for the concept in the table above; extend rather than duplicate.
2. Write the rule in the domain or application layer, not in endpoints or Razor/Flutter.
3. Add/adjust `ModelConfiguration.cs` (FK, unique index, `Restrict` delete), then `dotnet ef migrations add`
   (command in `DEVELOPMENT.md`) and review the generated migration.
4. Expose DTOs in `src/StudentSync.Contracts`, endpoints in the right `MapGroup`, permission check first line.
5. Extend `DemoDataSeeder` idempotently (fictional data, `example.test` emails).
6. Tests: unit for the rule, integration for authorization/tenant boundary, browser for the UI.
7. Update `IMPLEMENTATION_STATUS.md` and, if terminology changed, this skill.

## Required tests

- Unit (`tests/StudentSync.UnitTests`): the pure rule (e.g. deduction amount, allowed status transition).
- Integration (`tests/StudentSync.IntegrationTests`, Testcontainers SQL Server 2022): other institute → 404,
  wrong role → 403, student cannot read another student's record, idempotent replay.
- Browser: via `puppeteer-browser-testing` for every user-visible flow.

## Acceptance and reporting checklist

- [ ] Rule lives server-side and is covered by a unit or integration test that was run.
- [ ] Tenant, role and ownership checks in place; cross-boundary test added.
- [ ] Migration reviewed; `seed-demo` runs twice without duplicates.
- [ ] Terminology matches this table; `IMPLEMENTATION_STATUS.md` updated.
- [ ] Report: Completed / Verification (exact commands, counts) / Not completed / Next step.
