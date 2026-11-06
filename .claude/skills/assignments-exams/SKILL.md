---
name: assignments-exams
description: Use for the question bank (incl. image-based questions), assignments and submissions, timed exams with server-enforced deadlines, answer snapshots, auto/manual marking and feedback.
---

# Assignments, question bank and examinations

> **Flutter is parked this phase (owner decision 2026-11-04).** Student Sync currently ships
> the web portals and the installable PWA only. Treat every Flutter/mobile-app instruction in
> this skill as suspended: do not build, test, mirror or verify anything under
> `mobile/student_sync` until the owner re-enables the app.

## Purpose and triggers

Covers master prompt §11–§12 and CLAUDE.md addendum G. Use for any change to questions,
assignments, submissions, exams, attempts, timers, marking, results or feedback — including the
diagnostic-test reuse described in `student-onboarding-assessment`.

## Inspect first

- `src/StudentSync.Domain/Assessments/Assessments.cs` (`QuestionType` ×8, `Question`,
  `QuestionOption`, `Exam`, `ExamQuestion`, `ExamRecipient`, `ExamAttempt` with `DeadlineUtc` and
  `QuestionOrder`, `ExamAnswer`), `Domain/Assignments/Assignments.cs` (`Assignment`,
  `AssignmentRecipient`, `AssignmentAttachment`, `AssignmentSubmission`, `SubmissionFile`).
- `src/StudentSync.Application/Assessments/ExamService.cs` (`StartAsync` computes the server
  deadline and shuffles `QuestionOrder`; `SaveAnswerAsync` autosave; `SubmitAsync` idempotent;
  `GradeAnswerAsync`; `FinaliseExpiredAsync` run by `MaintenanceJobs.FinaliseExpiredExamsAsync` in
  `Application/Jobs/Jobs.cs`), `Assignments/AssignmentService.cs` (late/resubmission rules,
  return-for-correction, reminders), `Content/FileService.cs` (attachment/submission rules).
- Endpoints `/questions`, `/exams`, `/attempts`, `/assignments` in
  `src/StudentSync.Api/Endpoints/LearningEndpoints.cs` (`/my-submission` has `IdempotencyFilter`).
- Web: `Components/Pages/Learning/Questions.razor`, `ExamEdit.razor`, `ExamDetail.razor`,
  `TakeExam.razor`, `AttemptResult.razor`, `AssignmentEdit.razor`, `AssignmentDetail.razor`,
  `SubmissionEditor.razor`; `Shared/ExamsPanel.razor`, `AssignmentsPanel.razor`.
  Flutter: `features/learning/` (exam taking, submissions).
- Tests: `UnitTests/DomainTests.cs` (grading), `IntegrationTests/ApiTests.cs` (attempt, autosave,
  idempotent submit, answer keys hidden, assignment end-to-end).

## What exists / what is missing

Exists and verified: 8 question types with options/accepted answers/numeric tolerance; exams with
window, duration, attempts, shuffle, recipients (session/batch/students), publish, results release;
attempts with server deadline, autosave, idempotent submit, auto-grading of objective types, manual
grading, auto-finalise job; assignments with attachments, drafts, late policy, resubmission,
grading and return for corrections; file submissions (PDF/images/audio/DOCX).
Missing: **images on questions and options** (no `ImageFileId`), question attachments/diagrams;
a frozen **question/option snapshot per attempt** (today `QuestionOrder` stores ids and answers
reference `ExamQuestionId`, so a later edit to the bank item changes what the attempt shows);
assignment questions with marks (assignments are instructions + files only); rubric fields; practice
quizzes (unscheduled) if the owner wants them (§23).

## Rules that must hold (addendum G; master prompt §12)

- Server owns the timer: `StartedAtUtc`, `DeadlineUtc`, `SubmittedAtUtc` on the server; the client
  countdown is cosmetic; refresh/reconnect resumes the same attempt with remaining server time.
- Snapshot: at `StartAsync`, copy stem, options (text + order), marks and type into an
  `AttemptQuestionSnapshot` (JSON per `ExamAnswer` or a table). Grading and result views use the
  snapshot; editing the bank item after start never changes an existing attempt.
- Answer keys / `IsCorrect` are never serialised to students before `ResultsReleased`.
- Objective auto-marking only with a valid key; subjective answers require a teacher; any
  AI-assisted marking is advisory and stored separately until the teacher confirms.
- Students see only their own submissions/attempts/results; teachers see only recipients they teach.
- Assignments and exams stay distinct entities; do not merge them.

## Workflow

1. Images: add `ImageFileId` to `Question` and `QuestionOption` (`FilePurpose.QuestionImage`,
   PNG/JPEG/WebP ≤ 5 MB, metadata stripped via `ImageMetadataStripper`), served through `/files`
   with recipient-bound tokens; same for `Assignment` instructions images (reuse `AssignmentAttachment`).
2. Snapshot: add `SnapshotJson` to `ExamAnswer` rows created at start (one per question in
   `QuestionOrder`) or a new `AttemptQuestion` table; migrate `GetAttemptAsync`, `ResultAsync`,
   grading and the Blazor/Flutter attempt views to read from it.
3. Assignment questions (optional per task): `AssignmentQuestion` (reuse `Question`) with marks and
   per-question `SubmissionAnswer`; keep file/text submission paths working.
4. Marking UI: teacher review page listing submissions/attempts with inline marks, comments,
   "request correction"; student result page shows feedback only after release.
5. Seed: one exam with an image question, an attempt whose source question is later edited (for the
   snapshot test), one assignment with questions.

## Security and permissions

- `ExamsManage`/`AssignmentsManage` for authoring and marking; `ExamsTake`/`AssignmentsSubmit` for
  students, limited to recipients; tenant filter plus explicit recipient check.
- Image files are private; validate signature and size; never accept data URLs in stems.
- Prevent duplicate active attempts unless `MaxAttempts` allows; `SubmitAsync` stays idempotent.

## Required tests

- Unit: snapshot immutability (edit after start → attempt unchanged); marks aggregation; late rules.
- Integration: start → autosave → refresh (same attempt, remaining time from server) → expiry
  finalised by job; teacher edits question after start, student result unchanged; image question
  renders a token link only for recipients; student A cannot read student B's attempt/submission
  (404); keys hidden before release; other institute 404.
- Browser (puppeteer): teacher creates an exam with an image question; student takes it with a
  countdown, refreshes mid-attempt, submits; teacher marks a long answer; student sees feedback —
  desktop and 375 px, console clean.

## Acceptance / reporting checklist

- [ ] Addendum N: "exam time enforced server-side incl. refresh/reconnect" and "submitted snapshot
      unchanged if the bank item changes" pass; students cannot access others' work.
- [ ] Existing exam/assignment tests still pass (counts); `USER_GUIDE.md` updated.
- [ ] Report: commands, counts, screenshots, unverified items.
