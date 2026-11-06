---
name: document-chapter-processing
description: Use for student-supplied books/resources (PDF, PPT/PPTX, DOC/DOCX, images) — secure upload, background text extraction, scanned-PDF/OCR detection, AI-proposed chapter boundaries and page ranges, teacher review/correction, and publication to students.
---

# Book upload and chapter-wise processing

> **Flutter is parked this phase (owner decision 2026-11-04).** Student Sync currently ships
> the web portals and the installable PWA only. Treat every Flutter/mobile-app instruction in
> this skill as suspended: do not build, test, mirror or verify anything under
> `mobile/student_sync` until the owner re-enables the app.

## Purpose and triggers

Covers master prompt §13 and CLAUDE.md addendum H: students bring NCERT / Kerala State / other
books at onboarding; teachers upload them; a pipeline proposes chapters that the teacher reviews
before students see them. Use for any work on resource uploads, document jobs, extraction/OCR,
chapter detection, chapter review UI, or chapter publication.

## Inspect first

- `src/StudentSync.Domain/Content/Content.cs` (`StoredFile`, `FilePurpose`, `LearningMaterial`
  with `SubjectId`/`ChapterId`), `Domain/Academics/Academics.cs` (`Chapter`, `Topic` — the
  catalogue chapters; extracted book chapters must link to, not replace, them),
  `Domain/AI/AiAssistant.cs` (`AiProcessingJob`, `AiJobStatus`, `AiUsageRecord` — the job pattern to copy).
- `src/StudentSync.Application/Content/FileService.cs` (per-purpose MIME/size/magic-number rules;
  `Material` already allows PDF/DOCX/PPTX/XLSX/images ≤ 50 MB), `Content/ContentService.cs`,
  `AI/AiJobRunner.cs`, `AI/AiService.cs`, `Common/Abstractions.cs` (`IFileStorage`,
  `IBackgroundJobs`, `ILanguageModel`), `src/StudentSync.Infrastructure/Services/Adapters.cs`
  (`LocalFileStorage`, `HangfireBackgroundJobs`, `NotConfiguredLanguageModel`),
  `Services/ClaudeLanguageModel.cs`.
- `Directory.Packages.props` before adding any PDF/Office/OCR package.
- Web: `Components/Pages/Learning/Hub/MaterialsTab.razor`, `Student/Library.razor`,
  `Student/SubjectDetail.razor` (syllabus in chapter order), `Learning/AiDraftReview.razor`
  (review/approve pattern to mirror). Flutter: `features/subjects/`, `features/learning/`.
- `IMPLEMENTATION_STATUS.md`: Claude provider inactive (no key); no malware scanning.

## What exists / what is missing

Exists: private file storage with validation and user-bound links; materials scoped to subject /
chapter; catalogue chapters and syllabus views; an AI job model with queued/running/failed/blocked
states, retries, idempotency, usage tracking and teacher review/approve/publish of drafts.
Missing: `OriginalDocument`/book entity with curriculum, grade, subject, edition, language and
academic-year metadata; `DocumentProcessingJob`; text/page extraction; scanned-PDF detection; OCR
provider abstraction; `ExtractedChapter` with page ranges, confidence and review state; teacher
review UI (rename/merge/split/reorder/reject); chapter publication to specific students.

## Rules that must hold (addendum H; master prompt §13)

- Validate extension, MIME, signature, size and uploader permission before storing; keep the
  original file unchanged and versioned; re-processing a replaced file must not duplicate chapters
  (upsert by document version + page range, or replace the unreviewed set).
- Extraction and AI output are **drafts**: nothing is visible to students until a teacher
  publishes; never fabricate or rewrite textbook content; show confidence/warnings
  (e.g. "scanned pages, OCR needed", "low-confidence headings").
- A scanned PDF (no text layer) is reported as *needs OCR*, not treated as empty text. If no OCR
  provider is configured, the job ends `Blocked` with a clear message and manual chapter entry remains possible.
- Student documents and identifiable data go to an external AI/OCR provider only through the
  configured `ILanguageModel`/new `IOcrProvider` adapter and only when the institute has enabled it
  (feature flag + consent setting). Send page text, never student identity.
- Each `ExtractedChapter` links back to `DocumentId` + `FromPage..ToPage` so the teacher can verify.
- Long work runs in Hangfire jobs via `IBackgroundJobs`, idempotent per (document, version).

## Workflow

1. Entities: `LearningDocument` (StudentId?, SubjectId?, CurriculumId, GradeLevelId, Title,
   Edition, Language, FileId, Version, Status), `DocumentProcessingJob` (mirror `AiProcessingJob`),
   `DocumentPage` (page no, text, HasTextLayer), `ExtractedChapter` (title, order, from/to page,
   confidence, ReviewStatus, CatalogueChapterId?, PublishedAtUtc). Migration reviewed.
2. Extraction adapter `IDocumentTextExtractor` (PDF first; PPTX/DOCX via OpenXML; images → OCR path)
   and `IOcrProvider` with a `NotConfiguredOcrProvider`. Justify any NuGet package (licence,
   maintenance) in the report.
3. Chapter detection: heuristic pass (font-size/“Chapter N”/numbering, table of contents page)
   first; optional `ILanguageModel` pass that receives page text only and returns a validated JSON
   structure (reuse the structured-output validation style in `AiService`).
4. `DocumentService`: upload → job enqueue; status; chapters list; review operations (rename, merge,
   split at page, reorder, reject, map to catalogue chapter); publish to the student(s).
5. Endpoints `/documents`, `/documents/{id}/chapters`, `/students/{id}/documents`, `/me/documents`.
6. UI: upload with progress and status; review screen showing proposed chapters next to a page
   viewer (PDF.js is already not in the repo — check `DESIGN.md`/`Directory.Packages.props` before
   adding); student book view grouped by chapter with "open at page". Follow the frontend skills.
7. Seed: one small fictional public-domain-style PDF with 3 detectable chapters as demo content
   (text-based, no real student data).

## Security and permissions

- `ContentManage` to upload/review/publish; owners: the student the document was uploaded for,
  their teachers (`StudentsTaughtBy`), admins. Students read published chapters of their own
  documents only. Other institute → 404.
- Private links only; strip metadata from images; limit page count/size per job; log no file contents.

## Required tests

- Unit: heading-detection heuristics on fixture text; scanned-PDF detection (no text layer);
  JSON validation of model output; re-process idempotency.
- Integration: upload PDF → job runs (in-process test runner) → chapters proposed → teacher renames
  and merges → publish → student sees chapters; unpublished chapters 404 for student; scanned fixture
  → `NeedsOcr`/Blocked with message; other student 404; replace file → no duplicate chapters.
- Browser (puppeteer): upload, watch status, review, publish, student opens a chapter — desktop and 375 px.

## Acceptance / reporting checklist

- [ ] Addendum N: originals remain available and chapters reviewable; scanned PDFs flagged for OCR.
- [ ] Providers behind interfaces; no external call without configuration; no student identity sent.
- [ ] Report lists packages added (with reasons), tests run, screenshots, and unverified parts.
