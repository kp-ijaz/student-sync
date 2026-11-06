---
name: attendance-session-management
description: Use for session participants, attendance marking and correction, session completion, billing eligibility of a session/participant, and the audit history around them.
---

# Attendance and session management

> **Flutter is parked this phase (owner decision 2026-11-04).** Student Sync currently ships
> the web portals and the installable PWA only. Treat every Flutter/mobile-app instruction in
> this skill as suspended: do not build, test, mirror or verify anything under
> `mobile/student_sync` until the owner re-enables the app.

## Purpose and triggers

Covers master prompt §9 and CLAUDE.md addendum E (attendance) and the link to A (hour
consumption). Use for: marking attendance (one student or a whole class), correcting it, session
status changes (Live/Completed/Cancelled), participant add/remove, and deciding whether a completed
session is billable for each participant.

## Inspect first

- `src/StudentSync.Domain/Scheduling/Sessions.cs` (`SessionStatus`, `ParticipantStatus`,
  `AttendanceStatus { Present, Late, Absent, Excused }`, `AttendanceRecord`, `SessionScheduleChange`).
- `src/StudentSync.Infrastructure/Persistence/ModelConfiguration.cs` — `AttendanceRecord` has a
  unique index on `(SessionId, StudentId)`.
- `src/StudentSync.Application/Scheduling/AttendanceService.cs` (`RosterAsync`, `MarkAsync`),
  `SchedulingService.cs` (`SetStatusAsync`, `AddParticipantAsync`, `RemoveParticipantAsync`),
  `Common/AccessPolicy.cs` (`GetManageableSessionAsync`), `Common/AuditAndNotify.cs`.
- Endpoints under `/sessions/{id}/attendance` in `src/StudentSync.Api/Endpoints/LearningEndpoints.cs`.
- Web: `Components/Pages/Learning/Hub/AttendanceTab.razor`, `SessionHub.razor`,
  `Teacher/TeacherDashboard.razor` (pending attendance), `Shared/AttendanceBars.razor`.
  Flutter: `features/sessions/` attendance editor, `features/teacher/`.
- Tests: `tests/StudentSync.IntegrationTests/ApiTests.cs` (attendance for one student),
  `tests/e2e/tests/journeys.spec.ts` (attendance journey).

## What exists / what is missing

Exists: roster + bulk mark for any number of participants, four statuses, `MarkedAtUtc`/`MarkedByUserId`,
note, uniqueness per participant/session, `AttendanceMarked` notification, teacher/admin management
scope, participant add/remove with history, status transitions.
Missing: correction history (today an edit overwrites the row — no previous-value trail beyond
`AuditLog`), admin correction UI, "attendance to complete" exceptions list, billing eligibility per
participant and its link to session completion, configurable extra statuses, teacher substitution.

## Rules that must hold

- One attendance row per (session, student) — keep the unique index; corrections update the row
  **and** append an `AttendanceCorrection` (previous status, new status, reason, by, at).
- Attendance can only be marked for active participants of a session the user may manage
  (`GetManageableSessionAsync`); admins may correct any session in the institute, audited.
- Completing a session does not by itself charge hours; `BillingStatus` on each participant is
  derived from attendance + institute policy by the hour-package-ledger slice (see that skill).
  Cancelled sessions are never billable. Do not assume "present ⇒ charged" or "absent ⇒ not charged"
  without reading the policy.
- Status transitions: Scheduled → Live → Completed; Scheduled/Live → Cancelled; Completed is
  terminal except for admin reopen with reason. Reject anything else in the domain layer.
- Rescheduling and participant changes keep `SessionScheduleChange` history; never delete rows.

## Workflow

1. Add `AttendanceCorrection` entity (+ FK to `AttendanceRecord`, `Restrict`), migration.
2. `AttendanceService.MarkAsync`: when a row exists and the status changes, write a correction
   with the caller's reason (required for admins, optional for the marking teacher within N hours —
   make N a setting). `AuditService.Record("attendance.corrected", …)`.
3. Add `HistoryAsync(sessionId)` and an admin "Attendance exceptions" query (completed sessions with
   unmarked participants; sessions marked after a configurable delay).
4. Session completion: `SetStatusAsync(Completed)` validates all participants are marked (or lets
   the admin complete with explicit "mark remaining as absent"), then raises the billing hook.
5. UI: bulk "all present" then per-row changes on `AttendanceTab.razor`; correction dialog with
   reason; history list; admin exceptions page; Flutter attendance editor gets the same reason field.
6. Dashboard: teacher "attendance to complete", admin "attendance exceptions" (addendum J).

## Security and permissions

- `AttendanceMark` permission plus session scope; students read only their own attendance
  (`/me/attendance`), never classmates'.
- Corrections by anyone other than an admin are limited to the marking teacher; all are audited.
- Tenant filter + explicit participant membership check before writing.

## Required tests

- Unit: status transition table; correction-window rule; "complete requires all marked" rule.
- Integration: mark one student in a 1:1 session (no batch); bulk mark a batch; re-mark creates one
  correction and still one attendance row; teacher of another session → 403; other institute → 404;
  complete with unmarked participant is rejected unless admin override; cancelled session cannot be marked.
- Browser (puppeteer): teacher marks a class from today's timetable, corrects one student with a
  reason, admin sees the exception list — desktop and 375 px, no console errors.

## Acceptance / reporting checklist

- [ ] Addendum N: "attendance unique per participant/session and corrections auditable" passes.
- [ ] Existing attendance integration + E2E tests still pass (state counts).
- [ ] `USER_GUIDE.md` attendance section updated; report lists verification and gaps.
