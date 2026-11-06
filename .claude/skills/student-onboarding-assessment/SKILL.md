---
name: student-onboarding-assessment
description: Use for admin-created student profiles, credential issuance, curriculum/grade/syllabus, weak-subject (learning-need) capture, and diagnostic assessments (authoring, paper/OMR manual marking, digital attempts, result review).
---

# Student onboarding and diagnostic assessment

> **Flutter is parked this phase (owner decision 2026-11-04).** Student Sync currently ships
> the web portals and the installable PWA only. Treat every Flutter/mobile-app instruction in
> this skill as suspended: do not build, test, mirror or verify anything under
> `mobile/student_sync` until the owner re-enables the app.

## Purpose and triggers

Covers the start of the student journey (master prompt §2 steps 1–5, §3–§5; CLAUDE.md addendum
B and C). Use it for: creating/editing students and their login accounts, password reset /
activation / suspension, recording grade, syllabus and subjects, capturing weak subjects from
the student or a parent, and building, assigning, taking, marking and reviewing a diagnostic test.

## Inspect first

- `src/StudentSync.Domain/People/People.cs` (`Student`, `PersonStatus` — still contains
  `PendingApproval`, to be retired), `Academics/Academics.cs` (`GradeLevel`, `Course`, `Subject`,
  `StudentSubject`), `Academics/StudentCurriculum.cs`, `Assessments/Assessments.cs`.
- `src/StudentSync.Application/People/PeopleService.cs` (`CreateStudentAsync` → temporary password,
  `UpdateStudentAsync`, registration approve/reject to remove), `People/RegistrationService.cs`
  (self-registration — to remove), `Academics/StudentCurriculumService.cs`, `Assessments/ExamService.cs`.
- `src/StudentSync.Infrastructure/Identity/AuthService.cs` (`MustChangePassword` gate, lockout),
  `IUserAccountService` in `Application/Common/Abstractions.cs`.
- Endpoints: `src/StudentSync.Api/Endpoints/CoreEndpoints.cs` (`/auth/register`, `/students`,
  `/students/{id}/subjects`, `/registrations`), `LearningEndpoints.cs` (`/questions`, `/exams`, `/attempts`).
- Web: `src/StudentSync.Web/Components/Pages/Admin/Students.razor`, `StudentProfile.razor`,
  `StudentSubjectsPanel.razor`, `Registrations.razor`, `Account/Register.razor`,
  `Components/Shared/TempPasswordDialog.razor`. Flutter: `mobile/student_sync/lib/features/auth/register_screen.dart`.
- Tests: `tests/StudentSync.IntegrationTests/ApiTests.cs` (registration + temp-password cases),
  `CurriculumTests.cs`; `tests/e2e/tests/journeys.spec.ts` (register journey).

## What exists / what is missing

Exists and must be reused: admin student creation with one-time temporary password and forced change
at first login; `PersonStatus` + `SetActiveAsync`; `GradeLevel`; NCERT subject structure
(`Subject.Kind`, choice groups) and per-student `StudentSubject`; full question bank and exam engine
(8 question types, recipients, timed attempts, autosave, server deadline, manual grading, results release).

Missing: a `Curriculum`/syllabus entity (NCERT, Kerala State, …) on the student and on subjects/books;
academic year on the student; a weak-subject / learning-need record with source
(student / parent-via-admin / diagnostic) and review/override audit; a "diagnostic" assessment
kind with per-subject/topic result breakdown and an on-paper (OMR-style) answer-entry workflow;
`Suspended` account state; admin password reset endpoint. The self-registration path exists and
must be removed per addendum B.

## Rules that must hold (addendum B, C; master prompt §3–§5)

- No public sign-up: remove `/auth/register`, `/auth/registration`, `/registrations/*`,
  `Register.razor`, `Registrations.razor`, the Flutter register screen and the `PendingApproval` state
  (migration must map any such rows to `Inactive`). Only `UsersManage` can create students.
- Profile and login account are distinct steps; a failure in one must not leave an orphan
  (wrap in a transaction, or create profile first and report an actionable error).
- Never return or store a plaintext password after issuance; audit `student.created`,
  `student.credentials_issued`, `student.password_reset`, `student.status` without secrets.
- A student may have many curriculum subjects and many weak subjects; weak subjects work without a test.
- Diagnostic results are recommendations: staff can override with a recorded reason; they must not
  restrict the student. Answer keys are never sent to students before results release.
- Paper/OMR: staff enter the student's marked options against the key; do **not** advertise
  automatic OMR scanning (no OCR/OMR integration exists).

## Workflow

1. Model: add `Curriculum` (name, code), `Student.CurriculumId`, `Student.AcademicYearId`,
   `StudentLearningNeed` (StudentId, SubjectId, optional TopicId, Source enum, Note, Status,
   ReviewedByUserId, ReviewReason). Reuse `Exam` with a new `Purpose`/`Kind` (Diagnostic) rather than a
   second exam engine; add `ExamAttempt.EntryMode` (Digital / PaperEntered) and `EnteredByUserId`.
2. Configure in `ModelConfiguration.cs` (unique `(StudentId, SubjectId, TopicId)` for needs); migration.
3. Services: extend `PeopleService` (create/update, reset password, suspend), `StudentCurriculumService`
   (curriculum + needs), `ExamService` (diagnostic kind, paper entry, per-subject result summary).
4. Endpoints under `/students/{id}/…` (admin) and `/me/learning-needs` (student self-report), with
   `AccessPolicy` checks; teachers read needs only for students they teach.
5. Web: student create/edit dialog gains curriculum, academic year, subjects, weak subjects; a
   "Diagnostic" tab on `StudentProfile.razor`; paper-entry screen reuses exam grading components.
   Follow `frontend-design`, `ui-ux-design-system`, `responsive-ui-implementation`.
6. Seed: curricula (NCERT, Kerala State), one demo diagnostic test, needs for demo students.
7. Remove self-registration code and its tests; replace with admin-provisioning tests.

## Security and permissions

- `UsersManage` for profile/account operations; `ExamsManage` for authoring/marking; students only
  `/me/*`. Tenant filter applies automatically — still assert cross-institute 404 in tests.
- Validate `CurriculumId`, `GradeLevelId`, `SubjectId` belong to the tenant before saving.
- Rate-limit and audit password resets; `MustChangePassword = true` after any admin reset.

## Required tests

- Unit: learning-need source/override rules; diagnostic result aggregation by subject/topic.
- Integration: admin creates student → temp password → forced change; teacher cannot create a student;
  `/auth/register` returns 404; student self-reports two weak subjects; paper entry marks correctly
  and replay is idempotent; student cannot see the key before release; other institute → 404.
- Browser (puppeteer): create student → credentials dialog → sign in as that student at desktop and 375 px.

## Acceptance / reporting checklist

- [ ] Addendum N: "multiple weak subjects and multiple curriculum subjects", "diagnostic created,
      attempted, marked, reviewed without exposing answers" both covered by tests that ran.
- [ ] No registration route, page, screen or test remains; docs (`README`, `USER_GUIDE`,
      `IMPLEMENTATION_STATUS`) updated.
- [ ] Report lists commands run, counts, screenshots, and anything not verified.
