---
name: teacher-allocation-scheduling
description: Use for student→teacher requests and admin per-subject teacher assignment (with history), teacher availability, timetables, session scheduling in all teaching modes, time zones, recurrence, conflicts and rescheduling.
---

# Teacher allocation and scheduling

> **Flutter is parked this phase (owner decision 2026-11-04).** Student Sync currently ships
> the web portals and the installable PWA only. Treat every Flutter/mobile-app instruction in
> this skill as suspended: do not build, test, mirror or verify anything under
> `mobile/student_sync` until the owner re-enables the app.

## Purpose and triggers

Covers master prompt §7–§8 and CLAUDE.md addendum D–E: a student may request a teacher they
know; an admin assigns teachers per subject; sessions are then scheduled against teacher and
student availability. Use for any change to assignments, availability, timetable views, session
creation/reschedule/cancel, participants, or conflict detection.

## Inspect first

- `src/StudentSync.Domain/Academics/Academics.cs` (`TeacherSubject`, `TeachingArrangement`,
  `TeachingArrangementStudent`, `Batch`), `Domain/Scheduling/Sessions.cs` (`SessionType`,
  `LearningSession`, `SessionSeries`, `SessionParticipant`, `SessionScheduleChange`,
  `TeacherAvailability`), `Scheduling/Recurrence.cs`, `Scheduling/ZonedTime.cs`.
- `src/StudentSync.Application/Scheduling/SchedulingService.cs` (`CheckAsync` conflicts,
  `CreateAsync`, `RescheduleAsync`, `CancelAsync`, `AddParticipantAsync`, `AvailabilityAsync`,
  `ExportIcsAsync`), `Common/AccessPolicy.cs` (`StudentsTaughtBy`, `VisibleSessionsAsync`).
- Endpoints `/sessions`, `/teachers`, `/academics` in `src/StudentSync.Api/Endpoints/LearningEndpoints.cs`
  and `CoreEndpoints.cs`.
- Web: `Components/Pages/Learning/NewSession.razor`, `Sessions.razor`, `Admin/Arrangements.razor`,
  `Teacher/Availability.razor`, `Teacher/TeacherDashboard.razor`, `Student/Timetable.razor`,
  `Shared/MonthGrid.razor`, `Shared/TimeZoneSelect.razor`. Flutter: `features/sessions/`, `features/teacher/`.
- Tests: `tests/StudentSync.UnitTests/DomainTests.cs` (DST, recurrence), `IntegrationTests/ApiTests.cs`
  (1:1 without batch, double-booking blocked, DST series).

## What exists / what is missing

Exists (verified by tests): all four teaching modes, nullable `BatchId`, explicit participants,
recurrence materialised up-front (≤400 occurrences), DST-safe conversion, holidays, teacher
double-booking check, reschedule/cancel with `SessionScheduleChange` history, weekly
`TeacherAvailability`, ICS export, hourly timetable views on web and Flutter.
Missing: a student-teacher-**subject** assignment with status (Requested / Approved / Active /
Ended), effective dates, who approved, and history; a student "preferred teacher" request flow;
teacher capacity; student-availability conflict check; teacher substitution with history.

## Rules that must hold (addendum D–E)

- No single mandatory `TeacherId` on `Student`. Model `StudentTeacherAssignment`
  (StudentId, TeacherId, SubjectId, Status, RequestedByUserId, ApprovedByUserId, EffectiveFrom,
  EffectiveTo, Reason). A request is not an assignment until approved by `UsersManage`/`AcademicsManage`.
- Different subjects may have different teachers; only one *active* assignment per (student, subject).
- `AccessPolicy.StudentsTaughtBy` must include active assignments so the teacher sees the student
  before the first session is scheduled.
- Sessions keep today's invariants: batch optional, participants explicit, UTC + `TimeZoneId`,
  recurrence time-zone aware, conflicts explained before saving, history preserved on reschedule.
- Scheduling a one-to-one or private-group session should default the teacher from the active
  assignment for that subject and warn (not block) when the teacher differs.
- Teacher substitution records a `SessionScheduleChange` (add `TeacherChanged` to `ScheduleChangeType`).

## Workflow

1. Entity + `ModelConfiguration.cs` (filtered unique index on active (StudentId, SubjectId); FKs
   `Restrict`), migration reviewed.
2. `TeacherAssignmentService`: request (student, `/me/teacher-requests`), approve/assign/end (admin),
   list by student/teacher, history. Audit each transition.
3. Extend `AccessPolicy.StudentsTaughtBy` with active assignments; add integration test.
4. Scheduling: default teacher from assignment; optional student-availability check (new
   `StudentAvailability` only if the owner wants it — otherwise conflict check against the student's
   existing sessions, which already exists).
5. UI: admin "Teachers" panel on `StudentProfile.razor` (assign per subject, approve requests),
   student "My teachers" with request action, teacher "My students" grouped by subject; timetable
   views unchanged unless the task says otherwise.
6. Notifications: request received (admin), request approved/changed (student, teacher).
7. Seed: assignments for demo students matching existing arrangements.

## Security and permissions

- Students: request only for themselves and only for subjects in their curriculum
  (`StudentCurriculum.SubjectsOf`). Teachers: read own assignments. Admin: everything, audited.
- Validate teacher, student and subject all belong to the tenant and the teacher has the
  `TeacherSubject` capability (warn or block per owner decision; default warn).

## Required tests

- Unit: assignment state machine; "one active per subject"; default-teacher rule.
- Integration: student requests teacher → admin assigns a different teacher; Maths and Physics get
  different teachers; teacher sees the student via `/teachers/me/students` before any session;
  other teacher → 403/404; other institute → 404; existing scheduling tests still pass.
- Browser (puppeteer): admin assigns teacher per subject; student sees "Your teachers"; teacher sees
  the student; schedule a 1:1 class at 1440 px and 375 px.

## Acceptance / reporting checklist

- [ ] Addendum N: "request a preferred teacher and admin can assign another", "different subjects can
      have different teachers" — tests ran and passed.
- [ ] No regression in scheduling/DST/recurrence suites (state counts).
- [ ] Report with commands, counts, screenshots, unverified items.
