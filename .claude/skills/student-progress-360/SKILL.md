---
name: student-progress-360
description: Use for the teacher's student-360 view, student learning history and progress by subject/topic, teacher observations, and the admin/teacher/student dashboards and reports built on them.
---

# Student 360, progress and dashboards

> **Flutter is parked this phase (owner decision 2026-11-04).** Student Sync currently ships
> the web portals and the installable PWA only. Treat every Flutter/mobile-app instruction in
> this skill as suspended: do not build, test, mirror or verify anything under
> `mobile/student_sync` until the owner re-enables the app.

## Purpose and triggers

Covers master prompt §14–§15 and CLAUDE.md addendum I–J: a teacher opens a student and sees
syllabus, weak areas, hours, schedule, attendance, class reports, resources, submissions, exam
answers and results; dashboards surface the next action for each role. Use for any change to
student profile views, progress metrics, observations, dashboards or reports.

## Inspect first

- `src/StudentSync.Application/People/PeopleService.cs` (`StudentProfileAsync` — courses, batches,
  arrangements), `Reporting/ProgressService.cs` (`ForStudentAsync` — evidence-based metrics with
  explanations), `Reporting/DashboardService.cs` (`AdminAsync`, `TeacherAsync`, `StudentAsync`,
  `AttendanceSummaryAsync`), `Reporting/ReportService.cs` (institution reports + CSV),
  `Common/AccessPolicy.cs` (`VisibleStudentIdsAsync`, `EnsureCanViewStudentAsync`).
- Contracts: `src/StudentSync.Contracts/PeopleAcademicsContracts.cs`, `EngagementContracts.cs`.
- Endpoints `/students/{id}`, `/dashboard`, `/reports`, `/teachers` in
  `src/StudentSync.Api/Endpoints/CoreEndpoints.cs`.
- Web: `Components/Pages/Admin/StudentProfile.razor`, `StudentSubjectsPanel.razor`,
  `Teacher/TeacherStudents.razor`, `Teacher/TeacherDashboard.razor`, `Admin/AdminDashboard.razor`,
  `Student/StudentDashboard.razor`, `Student/Progress.razor`, `Admin/Reports.razor`,
  `Shared/ProgressPanel.razor`, `StatCard.razor`, `MetricTile.razor`, `PillTabs.razor`.
  Flutter: `features/teacher/students_screen.dart`, `features/student/dashboard_api.dart`,
  `features/profile/`.
- `DESIGN.md` (dashboard and card rules), `docs/USER_GUIDE.md`.

## What exists / what is missing

Exists: admin student profile (courses, batches, arrangements, subjects panel, photo); teacher
"My students" limited by `StudentsTaughtBy`; progress with attendance, lessons, recordings watched,
assignments, exam outcomes, unresolved doubts, each with an explanation; role dashboards from live
data; institution reports with CSV; subject/syllabus views for students.
Missing: one teacher-facing **student 360 page** that composes grade/syllabus, weak subjects,
diagnostic results, hours (permission-gated), assignments per subject, exam attempts with answer
scripts, class-report history, observations and next steps; `TeacherObservation` entity; filters
(subject, syllabus, pending marking, attendance exceptions); admin dashboard items for onboarding,
credential issuance, diagnostics awaiting review, package/payment status, hour balances, attendance
exceptions, submissions awaiting marking; student dashboard items for remaining hours, assigned
teachers, learning plan, active exam countdown.

## Rules that must hold (addendum I–J; master prompt §14–§15)

- Teachers see only students they teach (`AccessPolicy.StudentsTaughtBy`, extended by the
  teacher-assignment slice) and only permitted sections; financial data (payments, prices) is
  admin-only; hour balance visibility to teachers is a setting (default: remaining hours only).
- Every metric states its definition, period and denominator (reuse the explanation pattern in
  `ProgressService`); no invented percentages or composite "scores"; recording metadata is never
  used to rank students.
- Students see only themselves; one student's results, answers or observations never reach another.
- Observations are teacher-authored, dated, per subject, editable by the author, auditable; they
  are not AI-generated and are not shown to students unless explicitly marked shareable.

## Workflow

1. Entity `TeacherObservation` (StudentId, TeacherId, SubjectId?, Text, NextSteps, VisibleToStudent,
   RowVersion); migration.
2. `StudentOverviewService.ForTeacherAsync(studentId)` returning one composed DTO with sections
   (profile, curriculum, needs, diagnostics, hours summary if permitted, upcoming/completed sessions,
   attendance summary, class-report history, resources/chapters, assignments, exams/attempts,
   observations) — each section a bounded, paged query; do not load everything unbounded.
3. Endpoints `/students/{id}/overview` (teacher/admin, permission-filtered server-side),
   `/students/{id}/observations` (CRUD), `/me/overview` for the student dashboard pieces.
4. Web: `Teacher/StudentOverview.razor` with pill tabs/sections, filters on `TeacherStudents.razor`;
   extend the three dashboards with the addendum J items as data-backed tiles that link to the
   action (not static numbers). Flutter: student detail page from the teacher Students tab.
5. Reports: add onboarding/hours/attendance-exception reports with CSV via `ReportService`.
6. Follow `frontend-design`, `ui-ux-design-system`, `responsive-ui-implementation`,
   `accessibility-audit`; profile pages are dense — check 375 px overflow.

## Security and permissions

- `UsersView` + `EnsureCanViewStudentAsync` on every section; per-section permission filter in the
  service (e.g. strip payments for teachers) rather than in Razor.
- Paginate lists (≤ 50 per section); CSV exports are admin-only and audited.

## Required tests

- Unit: metric definitions (denominators), observation visibility rule, section permission filter.
- Integration: teacher sees assigned student's overview; unassigned teacher → 404; teacher response
  contains no payment fields; student overview excludes other students; admin sees all sections;
  other institute 404; dashboards count the seeded items correctly.
- Browser (puppeteer): teacher opens a student from "My students", scrolls every section, filters by
  subject; dashboards at 1440 px and 375 px; axe check on the overview page.

## Acceptance / reporting checklist

- [ ] Addendum N: "teacher sees only authorized students and permitted records" passes.
- [ ] Each dashboard tile links to a real page and is backed by a query that was tested.
- [ ] `USER_GUIDE.md`, `IMPLEMENTATION_STATUS.md` updated; report with commands, counts, screenshots.
