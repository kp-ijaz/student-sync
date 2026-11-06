---
name: class-learning-content
description: Use for post-class reports, session notes, reference videos and uploaded class videos, attachments (PDF/PPT/DOC/images), draft→publish workflow, and who may see which content.
---

# Class reports, notes, videos and materials

> **Flutter is parked this phase (owner decision 2026-11-04).** Student Sync currently ships
> the web portals and the installable PWA only. Treat every Flutter/mobile-app instruction in
> this skill as suspended: do not build, test, mirror or verify anything under
> `mobile/student_sync` until the owner re-enables the app.

## Purpose and triggers

Covers master prompt §10 and CLAUDE.md addendum F: after a class the teacher records what was
taught, notes, homework, observations, reference videos and attachments, then publishes to the
right students. Use for any change to notes, materials, pre-recorded/reference videos, class
reports, or their visibility.

## Inspect first

- `src/StudentSync.Domain/Content/Content.cs` (`StoredFile`/`FilePurpose`, `SessionNote` with
  `NoteVisibility`/`PublicationStatus`, `LearningMaterial` (file or link; session/subject/chapter/batch
  scoped), `PrerecordedVideo` with unlock rule/override, `ClassRecording`).
- `src/StudentSync.Application/Content/ContentService.cs` (notes CRUD/publish, materials,
  recordings, library), `Content/VideoService.cs`, `Content/FileService.cs` (per-purpose size/type
  rules, magic-number checks), `Common/Abstractions.cs` (`IFileStorage`, `IFileAccessTokens`).
- Endpoints `/notes`, `/materials`, `/videos`, `/files`, `/sessions/{id}/videos` in
  `src/StudentSync.Api/Endpoints/LearningEndpoints.cs`.
- Web: `Components/Pages/Learning/Hub/NotesTab.razor`, `MaterialsTab.razor`, `RecordingsTab.razor`,
  `Hub/StudentClassView.razor`, `Learning/Videos.razor`, `Shared/MarkdownView.razor`,
  `Shared/VideoList.razor`, `Student/MediaPlayer.razor`. Flutter: `features/videos/`, `features/learning/`.
- Tests: `IntegrationTests/ApiTests.cs` (locked video has no link, forged file token 403, other
  student 404), unit unlock-rule tests in `UnitTests/DomainTests.cs`.
- `DESIGN.md` for the note/material card patterns.

## What exists / what is missing

Exists: Markdown notes with sanitised HTML and KaTeX, draft/publish, teacher-only vs participants
visibility; file and link materials scoped to session/subject/chapter/batch; private storage with
short-lived user-bound links; pre-recorded videos (MP4/MOV/WebM) with before/after-class unlock and
manual override enforced on file links; recordings with consent gate and retention; library views.
`FilePurpose.Material` already accepts PDF, DOCX, PPTX, XLSX, PNG/JPEG/WebP up to 50 MB.
Missing: a structured **class report** (topics taught, syllabus mapping, objectives, homework,
observations, understanding, next steps) as one record per session; reference-video *links* on a
report (external URL, e.g. YouTube) distinct from uploaded videos; per-student chronological class
history by subject; publication to "the assigned group" when there is no session.

## Rules that must hold (addendum F; master prompt §10)

- One `SessionClassReport` per session (unique `SessionId`), authored by the session teacher or an
  admin; drafts invisible to students; publishing sets `PublishedAtUtc` and notifies participants.
- Students read only published content for sessions they participate in (reuse
  `AccessPolicy.VisibleSessionsAsync`); teachers of other sessions get 404, not 403 (do not leak existence).
- Attachments go through `FileService` rules; never expose storage keys or public URLs; links are
  short-lived and bound to the user. External reference links are validated with `OptionalUrl`
  (http/https only) and rendered as links, never embedded unsanitised.
- Pre-recorded video lock rules are unchanged; a class report may reference existing videos.
- Reports are part of the student's learning history — never hard-delete; archive with audit.

## Workflow

1. Entity `SessionClassReport` (SessionId, Status, TopicsCovered, Objectives, NotesMarkdown,
   Homework, Observations, UnderstandingRating?, NextSteps, PublishedAtUtc, AuthorUserId,
   RowVersion), `ClassReportLink` (Url, Title), attachments via `LearningMaterial.SessionId`
   (reuse) or a `ClassReportAttachment` join — prefer reuse.
2. `ClassReportService`: get/save draft/publish/archive; participants' history query
   `ForStudentAsync(studentId, subjectId?)` ordered by session start.
3. Endpoints `/sessions/{id}/report` (GET/PUT/POST publish) and `/students/{id}/class-history`,
   `/me/class-history`.
4. UI: a single "Class report" page/tab in `SessionHub.razor` that lets the teacher write the report,
   attach files, add reference links and publish without leaving the page (master prompt §17);
   student sees the published report first on the class page; history timeline on the student
   profile and student Learning page. Flutter: report section on the class page.
5. Notifications: "Class notes published" with a dedupe key per report publish.
6. Seed: a published report for one completed demo session, a draft for another.

## Security and permissions

- `ContentManage` to write; session scope via `GetManageableSessionAsync`; `ContentView` + participant
  check to read. Sanitise Markdown through the existing `MarkdownView` pipeline.
- File access only through `/files/{id}` with tokens; attachment ownership validated per tenant.

## Required tests

- Unit: report status transitions; external link validation.
- Integration: teacher saves draft → student 404 → publish → student sees it; non-participant student
  404; other teacher 404; attachment link requires the participant's token; report appears in the
  student's class history in order; other institute 404.
- Browser (puppeteer): publish a report with a PDF and a reference link; student opens it and the
  attachment at desktop and 375 px; no console/network errors.

## Acceptance / reporting checklist

- [ ] Addendum N: "teacher can publish a class report with notes, attachments and reference videos" passed.
- [ ] Existing notes/material/video tests still pass (counts).
- [ ] `USER_GUIDE.md` + `IMPLEMENTATION_STATUS.md` updated; report lists what was and was not verified.
