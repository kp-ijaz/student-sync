# Institute workflow — repository audit, gap analysis and plan

Date: 2026-11-04. Source of requirements: `CLAUDE.md` addendum sections A–O and
`Student_Sync_Institute_Workflow_Master_Prompt.md`. This document records what the
repository already does, what the new operating model needs, and the order in which
the gaps will be closed. Update it as slices land.

## 1. Baseline (verified 2026-11-04)

| Check | Result |
|---|---|
| `dotnet build StudentSync.slnx` | 0 errors |
| `dotnet test --project tests/StudentSync.UnitTests` | 106 passed, 0 failed |
| `dotnet test --project tests/StudentSync.IntegrationTests` | 49 passed, 0 failed (SQL Server 2022 via Testcontainers; Docker must be running) |
| Playwright E2E (`tests/e2e`) | 18 spec cases incl. axe + visual baselines; last full run 43 passed (2026-11-03) |
| Git | branch `main` has **no commits**; everything is untracked |

Stack as found: .NET 10, EF Core, SQL Server 2022, Blazor Web App, Flutter, SignalR,
Hangfire, local file storage behind `IFileStorage`, Claude provider behind
`ILanguageModel` (no key configured). Four migrations (`InitialSchema`,
`PrerecordedVideos`, `ProfilePhotos`, `CurriculumSubjects`).

## 2. Gap analysis by workflow

Legend — **Done**: exists and tested. **Partial**: reusable base exists, needs extension.
**Missing**: no equivalent. **Remove**: contradicts the addendum.

### A. Commercial model — learning-hour packages

| Requirement | Status | Evidence / notes |
|---|---|---|
| Configurable packages (hours, price, expiry) | Missing | No `LearningPackage`; only SaaS `SubscriptionPlan`/`TenantSubscription` (platform billing, different concept) |
| Purchase + payment records (status, method, reference, refunds, discounts) | Missing | Fees module was never built (`fees_module` flag off) |
| Append-only hour ledger with reasons and references | Missing | — |
| Deduction on completed billable session, exactly once | Missing | `LearningSession` has no billing state; `SessionStatus.Completed` exists; `IdempotencyRecord` + `RowVersion` infrastructure exists to build on |
| Absence / cancellation / increment policy, configurable | Missing | Owner decisions still open (master prompt §23) |
| Remaining-hours display (student, teacher-360, admin) | Missing | — |

### B. Admin-only student accounts

| Requirement | Status | Evidence / notes |
|---|---|---|
| Admin creates student + account, temporary password, forced change at first login | Done | `PeopleService.CreateStudentAsync`, `MustChangePassword`, `TempPasswordDialog.razor`, audited |
| Admin activate / deactivate | Done | `PersonStatus.Active/Inactive/Archived` + `SetActiveAsync` |
| Admin password reset, suspend state | Partial | No admin-initiated reset endpoint; no `Suspended` state (Inactive is used) |
| **No** self-registration / `/register` / approval queue | **Remove** | Built 2026-11-01: `RegistrationService`, `POST /auth/register`, `GET /auth/registration`, `/students/registrations*`, `Register.razor`, `Registrations.razor`, menu entry, `PersonStatus.PendingApproval`, `NotificationType.RegistrationReceived`, Flutter `register_screen.dart`, tests in `ApiTests.cs`, `journeys.spec.ts`, Flutter widget test |

### C. Onboarding, curriculum and diagnostic assessment

| Requirement | Status | Evidence / notes |
|---|---|---|
| Grade / class, academic year | Done | `GradeLevel`, `AcademicYear`, `Student.GradeLevelId`, `Student.SchoolName` |
| Syllabus / curriculum (NCERT, Kerala State) | Missing | Subjects hang off `Course`; there is no curriculum/board entity. Seed has an NCERT-shaped Classes 1–12 catalogue only |
| Subjects per student incl. language choices / extras | Done | `StudentSubject`, `StudentCurriculum` rule, `/students/{id}/subjects` |
| Chapters / topics | Done | `Chapter`, `Topic` (chapter status via sessions) |
| Weak subjects / topics (self-reported, parent-reported via admin) | Missing | — |
| Learning goals, admin/teacher observations | Missing | — |
| Diagnostic assessment (title, grade, syllabus, subjects, basic questions, assign to a student) | Partial | `Exam` + `Question` bank + `ExamAttempt` cover timed digital tests; no "diagnostic" purpose, no result-by-topic recommendation, no override-with-reason |
| Paper / OMR with manual answer-key entry | Missing | Attempts can only be started by the student |
| Image / diagram questions | Missing | `Question` has no image file reference |

### D. Teacher preference and assignment

| Requirement | Status | Evidence / notes |
|---|---|---|
| Teacher ↔ subject capability | Done | `TeacherSubject` |
| Student-teacher-subject assignment with status, dates, history | Partial | `TeachingArrangement` (OneToOne / PrivateGroup, optional subject) is the closest; it has no request/approval state or history |
| Student requests a known teacher; admin approves or assigns another | Missing | — |
| Availability | Done | `TeacherAvailability`, `Availability.razor` |

### E. Scheduling and attendance

| Requirement | Status | Evidence / notes |
|---|---|---|
| All four teaching modes, batch optional, participants, recurrence, DST, conflicts, reschedule/cancel with history | Done | `LearningSession`, `SessionSeries`, `SessionParticipant`, `SessionScheduleChange`; unit + integration tests |
| Teacher daily/weekly timetable | Done (web + Flutter) | Student `Timetable.razor`; teacher dashboard + sessions list |
| Attendance per participant, unique, marked-by/at, note | Done | `AttendanceRecord` (unique index), audited |
| Attendance corrections with history | Partial | Overwrites in place; audit log records the change but no correction rows |
| Billing / hour-consumption state on a session | Missing | See A |
| Teacher substitution | Missing | Teacher is fixed per session |

### F. Class report, notes, reference videos, attachments

| Requirement | Status | Evidence / notes |
|---|---|---|
| Notes (Markdown, draft/publish, teacher-only or participants) | Done | `SessionNote` |
| Materials: files and links per session / subject / chapter | Done | `LearningMaterial`, `StoredFile` (PDF, images, office types allowed — verify list in `FileRules`) |
| Reference video links / uploaded videos | Done | `PrerecordedVideo` (upload, lock rules); link = `LearningMaterial(Kind=Link)` |
| Structured class report (topics taught, objectives, understanding, homework, next steps) | Missing | Notes are free text; no observation/understanding fields, no per-student history view |

### G. Assignments, question bank, exams

| Requirement | Status | Evidence / notes |
|---|---|---|
| Assignments to session / batch / selected students, attachments, drafts, late & resubmission rules, grading, return for correction | Done | `Assignment*`, `AssignmentSubmission`, `SubmissionFile` |
| Question bank, 8 question types, marks, explanations | Done | `Question`, `QuestionOption` |
| Timed exams: server start/deadline, autosave, idempotent submit, auto/manual grading, release results, auto-finalise job | Done | `Exam*`, `ExamAttempt`, `ExamAnswer`, Hangfire job |
| Image-based questions and assignment questions | Missing | No file reference on `Question`; assignments have attachments but no question list |
| Answer snapshot preserved when the bank question is edited | Missing | `ExamAttempt.QuestionOrder` stores ids only; a later edit changes what the attempt shows |
| Reconnect resumes the same attempt with server time | Done | Verified by integration tests |

### H. Books and AI chapter organisation

| Requirement | Status | Evidence / notes |
|---|---|---|
| Upload PDF / PPTX / DOCX / images with validation | Partial | `StoredFile` + magic-number checks exist for current purposes; no `Book`/`OriginalDocument` purpose |
| Background extraction, OCR detection, chapter detection, page ranges, teacher review, publish | Missing | `IBackgroundJobs`, `ILanguageModel` and the AI job/approval pattern (`AiProcessingJob`, draft → review → publish) are the base to reuse |

### I. Teacher student-360 view

| Requirement | Status | Evidence / notes |
|---|---|---|
| Teacher sees only students they teach | Done | `AccessPolicy.VisibleStudentIdsAsync` / `TeacherTeachesStudentAsync` |
| Profile: grade, subjects, schedule, attendance, assignments, exams, notes | Partial | Admin `StudentProfile.razor` has subjects/enrolments; teacher `TeacherStudents.razor` is a list; no single 360 page with attendance, submissions, exam scripts, hours, weak areas |

### J–L. Dashboards, roles, tenancy

| Requirement | Status | Evidence / notes |
|---|---|---|
| Admin / teacher / student dashboards from live data | Done | Need new tiles: hours, onboarding, diagnostics awaiting review, marking queue |
| Roles admin/teacher/student; parents cannot sign in | Done | Parent code retained, sign-in refused |
| Single institute; tenant plumbing retained | Done | Platform area still present (deferred removal) |

## 3. Decisions needed from the owner (master prompt §23)

These are not blockers for slices 1–2; they are needed before slice 3 (hour ledger)
is finalised. Until answered they will be implemented as **institute settings with
the stated default**.

| Question | Proposed default |
|---|---|
| Do packages expire? | No expiry (optional `ValidUntil`) |
| Absent / late student | Absent: not charged; Late: charged full session |
| Billing increment | Exact minutes, rounded to 0.25 h |
| Refund / transfer of unused hours | Admin manual adjustment with reason; no automatic refund |
| Group class charging | Each participant charged the session duration |
| Teacher change after assignment | Student may request; admin approves |
| Practice quizzes | Allowed (exam without a fixed window) |
| Retry limits | Per assignment/exam setting, as today |
| File limits | Existing `FileRules` limits; books up to 100 MB |
| AI / OCR provider | Claude via `ILanguageModel` for chaptering; OCR provider interface with no implementation until approved |
| Notification channels | In-app + email (Mailpit in dev); push not configured |

## 4. Implementation plan (vertical slices, in order)

Each slice ships domain + migration + API + authorization + web UI + tests + docs, and
leaves the app runnable. The Flutter app is **parked** (owner decision 2026-11-04): slices
deliver web + PWA only and do not touch `mobile/student_sync`.

| # | Slice | Scope | Depends on |
|---|---|---|---|
| 0 | **Docs and tooling** (this change) | CLAUDE.md addendum, institute master prompt, 20 skills, Puppeteer, this plan | — |
| 1 ✅ done 2026-11-04 | **Admin-only accounts** | Remove `/register`, approval queue, `PendingApproval` and their tests (Flutter register screen: left as-is, app parked); add admin password reset + `Suspended`; update E2E/visual baselines | 0 |
| 2 ✅ done 2026-11-04 (web) | **Student academic profile** | `Curriculum` (NCERT, Kerala State, extensible) on `Student`; `StudentLearningNeed` (weak subject/topic, source: student / parent-via-admin / diagnostic, notes); learning goals & staff observations; admin + student UI; `seed-demo` update | 1 |
| 3 ✅ done 2026-11-04 (web) | **Diagnostic assessment** | `Exam.Purpose = Diagnostic`, topic-wise result summary, recommendation → learning needs with override reason, paper/OMR manual answer entry by staff, image on `Question` | 2 |
| 4 | **Hour packages and ledger** | `LearningPackage`, `PackagePurchase`, `PaymentRecord`, `HourLedgerEntry`, institute billing policy settings, session billing state, deduction job on completion (idempotent, concurrency-tested), balances on dashboards | 2, owner answers |
| 5 | **Teacher allocation** | `StudentTeacherAssignment` (subject, status Requested/Approved/Ended, effective dates, history), student request UI, admin approve/assign UI, feeds `AccessPolicy` | 2 |
| 6 | **Timetable and attendance corrections** | Teacher day/week timetable page, attendance correction rows with reason, teacher substitution | 5 |
| 7 | **Class report** | `SessionClassReport` (topics, objectives, understanding, homework, next steps, attachments, reference videos), draft/publish, per-student chronological history | 6 |
| 8 | **Assignments and exams extensions** | Image-based questions, assignment question lists, attempt snapshots, marking queue | 3 |
| 9 | **Books → chapters** | `BookDocument` upload, extraction job (text / scanned detection), AI chapter proposal, teacher review (rename/merge/split/reorder), publish to student | 2, provider approval |
| 10 | **Student 360 and dashboards** | Teacher 360 page, admin operations tiles, student hours/plan tiles, reports | 4–9 |
| 11 | **Hardening** | Security review, accessibility, performance, backup drill, full regression | all |

Recommended first increment after slice 0: **slice 1 + slice 2** (both small, no
open business decisions, and every later slice builds on the student profile).
