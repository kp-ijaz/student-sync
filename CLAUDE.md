# CLAUDE.md — Student Sync

> Permanent working instructions for AI coding agents contributing to Student Sync.
> The companion `Student_Sync_Master_Prompt.md` is the detailed product, architecture,
> design, and implementation specification. Read it before making substantial changes.
> If this file and the master prompt appear to conflict, stop and report the conflict
> rather than silently choosing one.

## 1. Mission

Build **Student Sync**, a secure, polished, global, multi-tenant tuition and learning
management platform. Deliver production-quality functionality, not merely attractive
mockups or disconnected demos.

Work as a senior product engineer, software architect, UI/UX designer, security-minded
developer, and test engineer. Understand the existing repository before changing it.
Preserve working functionality and follow established project conventions unless there
is a clear, documented reason to improve them.

## 2. Mandatory first steps

Before implementing anything:

1. Inspect the repository structure, Git status, README files, solution/project files,
   package manifests, environment examples, migrations, tests, and existing design docs.
2. Identify the actual frameworks, versions, architecture, coding conventions, and
   implemented features. Do not assume the repository matches the target architecture.
3. Run the existing build and relevant tests where practical. Record current failures
   separately from failures introduced by your work.
4. Compare the repository with `Student_Sync_Master_Prompt.md` and prepare a concise
   gap analysis: implemented, partial, missing, risky, and blocked.
5. Propose a small, ordered implementation plan. Start with the highest-value safe
   increment and avoid a broad rewrite unless explicitly approved.
6. If the master prompt is not present, ask the user to provide it; do not invent
   missing product requirements.

Do not begin by replacing files, scaffolding a new application, changing frameworks,
or deleting existing code before the audit.

## 3. Source of truth and decision rules

- `Student_Sync_Master_Prompt.md` is the detailed product and technical specification.
- This `CLAUDE.md` contains the permanent working rules and critical invariants.
- Existing repository behaviour is evidence, not automatic proof that the behaviour is
  correct. Check it against the specification.
- Treat explicit user decisions as authoritative. Ask one focused question when a
  material ambiguity cannot be resolved safely.
- Do not silently omit requirements, weaken security, or substitute a simpler feature.
- Clearly label assumptions, limitations, temporary workarounds, and deferred work.
- Do not claim a feature, test, migration, deployment, or integration works unless
  you have actually verified it.

## 4. Target technology direction

Use the versions and choices confirmed in the master prompt and repository audit.
The target direction includes:

- Backend: ASP.NET Core Web API on .NET 10 LTS, Entity Framework Core, SQL Server 2022.
- Architecture: Clean Architecture principles in a modular monolith; keep module
  boundaries clear without introducing needless microservices.
- Web: Blazor Web App.
- Mobile: Flutter for native Android and iOS applications. **Parked this phase** — see
  owner decisions above; web + PWA only.
- Real-time: SignalR where live updates are needed.
- Background processing: Hangfire or the repository-approved equivalent.
- Client experience: responsive web and installable PWA (mobile-native workflows deferred).
- File/media storage: an abstraction over suitable object storage.
- Integrations: implement only through secure, replaceable adapters.

Do not upgrade, downgrade, or replace a framework simply because a newer version exists.
First verify compatibility with the existing repository and the master specification.

## 5. Product invariants — never break these

### Owner decisions that supersede the master prompt (2026-11-01)

- Student Sync is used by **one institute only** — not a white-label or multi-institution SaaS. Keep the existing
  tenant isolation in code (it is tested and harmless), but do not build new multi-institution or super-admin features.
- Roles in use: institution admin, teacher, student. **Parents cannot sign in** (data/code retained for now).
- The institute sells **prepaid learning-hour packages** usable across subjects (see addendum section A).
- **Student accounts are admin-created only** (owner decision 2026-11-04, replaces the earlier self-registration
  decision). There is no student self-registration, public registration page, or student account approval queue.
  The admin creates the student profile and issues login credentials directly to the student.
- **Pre-recorded class videos** are locked by default and unlock automatically before or after the live class,
  with manual teacher/admin override. Lock state is enforced server-side (including file links).
- Navigation is a **grouped side menu with sub-menus**, never top navigation. **On phones and tablets
  (< 1024 px) the web/PWA uses the Flutter app's shell** (owner request 2026-11-04: the PWA must look and
  work like the mobile app): page title in the app bar with the bell and avatar, the same floating four-tab
  bottom bar per role (`Navigation.Tabs`), and the grouped side menu as the drawer for everything else.
  Desktop keeps the docked side menu only. See `DESIGN.md` §4.
- **Flutter mobile app is parked for this phase** (owner decision 2026-11-04). Only the
  web portals (admin, teacher, student) and the installable PWA are in scope. The Flutter
  code in `mobile/student_sync` is kept unchanged but is commented out of CI, docs and
  the skill workflows; do not build, test, extend or "keep in parity" the Flutter app
  until the owner re-enables it. Every Flutter/mobile instruction elsewhere in this file
  and in the skills is suspended for the duration of this phase.

### Teaching formats

Student Sync must support all of these teaching modes:

- Batch teaching.
- Ad hoc group teaching.
- Private group teaching.
- One-to-one teaching.

A batch must **not** be a mandatory parent for every class, course, or session.
A session may exist without a batch. Keep `BatchId` nullable wherever the domain
requires it, and model actual session participation explicitly (for example, with a
session-participant join entity). Do not force private or ad hoc teaching into a fake
batch merely to satisfy a database relationship.

### Multi-tenancy

- Every tenant-owned record must be scoped to its institution/tenant.
- Enforce tenant isolation in server-side application and data-access paths, not only
  in the UI.
- Never trust a tenant ID supplied by a client without validating it against the
  authenticated user's authorized tenant context.
- Prevent cross-tenant reads, writes, exports, file access, and real-time events.
- Super-admin access must be explicit, authorized, audited, and separate from ordinary
  institution-admin access.

### Roles and authorization

Support the product roles defined in the master prompt, including SaaS super admin,
institution admin, teacher, student, and parent. Use server-side authorization and
least privilege. Hiding a button is not authorization.

### Scheduling and time zones

- Store timestamps in a consistent UTC representation.
- Preserve the relevant IANA time-zone identity for institutions/users/schedules where
  needed; do not rely only on a fixed UTC offset.
- Display times in the appropriate user or institution time zone.
- Handle daylight-saving transitions, recurring schedules, date boundaries, and
  rescheduling deliberately.
- Avoid ambiguous local-time conversions and duplicate/missed sessions.

### AI assistance and publication

- AI-generated notes, summaries, transcripts, feedback, and other educational content
  are drafts unless the specification explicitly says otherwise.
- Require the appropriate teacher review and approval before publishing student-facing
  material.
- Respect consent, access controls, retention rules, and privacy requirements for
  recordings, transcripts, and student data.
- Never expose one student's private information to another student or an unauthorized
  parent/teacher.

### Data integrity

- Use proper foreign keys, uniqueness constraints, validation, transactions, and
  concurrency handling.
- Avoid orphan records, duplicate records, accidental cascading deletion, and
  inconsistent denormalized data.
- Make seed operations repeatable and safe to run more than once.
- Add audit trails for sensitive administrative and data-changing operations.

## 6. Engineering standards

### Architecture and code quality

- Keep domain rules out of UI components and controllers.
- Keep dependencies pointing inward toward domain/application abstractions.
- Prefer small, cohesive modules and clear interfaces.
- Use dependency injection and typed configuration.
- Avoid giant services, god classes, duplicated business rules, and speculative
  abstractions.
- Use asynchronous I/O appropriately and propagate cancellation tokens where supported.
- Handle errors deliberately; do not swallow exceptions or return success-shaped
  responses for failed operations.
- Use structured logging. Never log passwords, tokens, secrets, or unnecessary
  sensitive student information.
- Use clear names and consistent formatting. Add comments for non-obvious reasoning,
  not to narrate obvious code.

### API and validation

- Validate all untrusted input on the server.
- Use consistent API response and error conventions already established by the project.
- Enforce authorization and tenant scoping for every relevant endpoint.
- Use pagination and bounded queries for potentially large collections.
- Prevent over-posting and mass assignment; map request models explicitly.
- Keep secrets in environment/configuration providers or a secret manager, never in
  source control.
- Make destructive and state-changing operations explicit and auditable.

### Database and migrations

- Review generated migrations before applying them.
- Never casually delete or rewrite migrations that may have been applied.
- Preserve existing data and provide safe migration paths.
- Add indexes based on actual query patterns and tenant-scoped access paths.
- Enforce important invariants in the database as well as application validation.
- Use transactions for multi-step operations that must succeed or fail together.
- Check delete behaviours carefully, especially for students, sessions, recordings,
  assessments, and audit records.

### Frontend and UX

- Follow the project's design system and maintain a consistent visual language.
- Use the approved Student Sync brand colours and typography from the master prompt
  and `DESIGN.md`; do not invent a competing design system.
- Build responsive, accessible, production-ready screens—not just static visual demos.
- Provide loading, empty, error, success, and permission-denied states.
- Use clear labels, helpful validation, sensible focus order, keyboard support, and
  accessible contrast.
- Make navigation and primary actions obvious. Avoid unnecessary decoration and
  inconsistent spacing.
- Reuse shared components where they improve consistency without making simple UI
  changes difficult.
- Ensure web layouts work on desktop, tablet, and mobile breakpoints.

### Flutter

> **Suspended this phase (owner decision 2026-11-04).** The rules below apply only once
> the owner re-enables the Flutter app. Until then, do no Flutter work.

- Keep the mobile app aligned with the web product's permissions, terminology, and
  core workflows.
- Use platform-appropriate navigation and interaction patterns.
- Handle loading, offline/network failure, authentication expiry, and empty states.
- Do not hardcode API URLs, credentials, or environment-specific values.
- Keep API contracts and shared validation expectations consistent with the backend.

## 7. Security, privacy, and child safety

Treat student and parent information as sensitive.

- Apply authentication, authorization, tenant isolation, and least privilege by default.
- Protect credentials with established password hashing and secure authentication flows.
- Use secure cookie/token handling appropriate to the selected client architecture.
- Protect against injection, XSS, CSRF where applicable, IDOR/BOLA, mass assignment,
  insecure direct file access, and privilege escalation.
- Validate file type, size, ownership, and access before upload/download.
- Use expiring or otherwise controlled access for private media.
- Keep recordings, meeting links, transcripts, and student records private by default.
- Obtain and enforce required consent for recording and AI processing.
- Minimize collected data and define retention/deletion behaviour.
- Audit privileged actions and sensitive data access where appropriate.
- Do not put secrets or personal data in test fixtures, logs, screenshots, or commits.
- Use fictional data in development and demos. Never use real student data for seeding.

If a requested implementation would weaken privacy, child safety, or tenant isolation,
explain the risk and propose a safe alternative before proceeding.

## 8. Testing is part of implementation

For every meaningful change:

1. Add or update unit tests for domain and application rules.
2. Add integration tests for API, persistence, authorization, and tenant boundaries
   where relevant.
3. Add UI/component tests for important interaction and validation behaviour.
4. Run the relevant test suite and report the exact result.
5. Run formatting, linting, static analysis, and build checks supported by the repo.
6. Test important flows in a real browser using the available browser automation
   tooling (Playwright preferred; use Puppeteer if that is the established tool).
7. Inspect the rendered result at desktop and mobile sizes.
8. Check browser console errors, failed network requests, broken navigation, and
   accessibility basics.
9. For visual changes, capture and inspect screenshots; use visual regression checks
   when available.

Do not say “tested” if you only reviewed the code. Distinguish tests that were run,
tests that passed, tests that failed, and tests that could not be run.

### Minimum regression checks when relevant

- Sign-in, sign-out, session expiry, and role-based navigation.
- Tenant A cannot access Tenant B data by changing an ID or request.
- Each role sees only permitted records and actions.
- Batch, ad hoc group, private group, and one-to-one sessions can be created and used.
- A session without a batch works end-to-end.
- Session participants and attendance remain correct after edits/rescheduling.
- Time-zone and daylight-saving cases behave correctly.
- Teacher approval is required before AI-generated student-facing content is published.
- Uploads, recordings, and private links enforce access permissions.
- PWA flows handle network and authentication failures gracefully (Flutter: parked).

## 9. Development data and demo accounts

- Use fictional, clearly identified development/demo records only.
- Make seeders deterministic and idempotent.
- Avoid duplicate users, institutions, sessions, and related records on repeated runs.
- Provide representative examples of every teaching mode, including sessions without
  a batch.
- Ensure seeded relationships are valid and tenant-consistent.
- Keep demo credentials out of production configuration and never reuse real passwords.
- Document any demo credentials in an explicitly development-only location.

## 10. Research and tool use

When current technical facts, package compatibility, vendor APIs, or external service
behaviour matter, verify them using authoritative, current documentation. Prefer
official documentation and release notes.

Use the repository's existing tools first. Browser automation, accessibility tooling,
visual regression, code review, and design tooling should support the actual workflow;
do not add dependencies merely to create the appearance of sophistication.

Before adding a package or external service:

- Explain why it is needed.
- Check compatibility, maintenance status, licence, security posture, and alternatives.
- Avoid duplicate libraries that solve a problem already handled by the project.
- Keep integrations behind abstractions where practical.

Never claim that a plugin, MCP server, skill, or tool is installed or available without
checking the current environment. Ask before making account-level or destructive changes.

## 11. Implementation workflow

Work in small, reviewable increments:

1. State the user-facing outcome and acceptance criteria.
2. Identify affected modules, data, APIs, permissions, and UI.
3. Inspect the existing implementation and tests.
4. Describe the intended change briefly.
5. Implement the smallest complete vertical slice.
6. Add migrations and tests where needed.
7. Run build, tests, and browser verification.
8. Review the diff for security, regressions, dead code, and accidental scope creep.
9. Update relevant documentation and `DESIGN.md` when applicable.
10. Report what changed, files/modules affected, commands run, test results, and
    remaining risks or next steps.

Do not leave core flows half-wired: a screen should not pretend to save when no API
exists, a button should not be inert, and a successful response should not be shown
when the underlying operation failed.

## 12. Git and change safety

- Inspect `git status` before editing.
- Never overwrite or discard user changes.
- Do not run destructive commands, reset branches, force-push, delete data, or apply
  production migrations without explicit authorization.
- Keep changes focused and reviewable.
- Do not commit unless asked.
- Do not include secrets, generated build artefacts, local databases, or private user
  data in commits.
- If unexpected user changes appear, stop and ask how to proceed.

## 13. Definition of done

A task is complete only when:

- The requested behaviour matches the master specification and acceptance criteria.
- Authorization and tenant isolation are enforced server-side.
- Relevant validation, error, loading, and empty states are handled.
- Data relationships and migrations are safe.
- Automated tests have been added or updated and the relevant checks have been run.
- Important UI flows have been verified in a browser when applicable.
- No known critical console errors, broken links, or inert primary actions remain.
- Documentation is updated where necessary.
- The final report accurately states what was and was not verified.

## 14. Communication rules

Be direct, precise, and transparent.

At the start of a substantial task, provide a short plan. Ask only for information that
is genuinely necessary to avoid a material mistake. Do not repeatedly ask the user to
confirm details already established in the specification.

When reporting completion, use this structure:

- **Completed:** user-visible and technical changes.
- **Verification:** exact build/test/browser checks and their outcomes.
- **Not completed / limitations:** anything still missing or unverified.
- **Next step:** the most useful next increment.

Do not exaggerate progress. If blocked, explain the blocker and the smallest action
needed to unblock the work.

---

## Quick start instruction for a new coding session

Read this file and `Student_Sync_Master_Prompt.md`. Then audit the repository before
editing. Report the current architecture, build/test baseline, and a prioritized gap
analysis. Do not start a rewrite. Recommend the first small, complete, testable
implementation increment and wait for direction if it would materially change scope.

---

# AUTHORITATIVE PRODUCT ADDENDUM — INSTITUTE OPERATING WORKFLOW

This addendum records the institute's confirmed operating model (owner decision,
2026-11-04). It is authoritative for the workflows below and overrides any older,
conflicting wording in this file or in an older product prompt. The detailed
companion specification is `Student_Sync_Institute_Workflow_Master_Prompt.md`.
If a conflict remains unclear, stop and ask rather than guessing.

## A. Product model and commercial rule

Student Sync is the operational and learning platform for one tuition institute. It is
not a course marketplace. The institute sells prepaid learning-hour packages, not
mandatory subject-specific courses.

- A student purchases a package containing a defined number of learning hours (for
  example, 30 hours).
- Those hours may be used across one or more subjects according to the student's
  learning plan and institute rules.
- Do not assume that one package equals one subject, one teacher, one batch, or one
  fixed course.
- Store package entitlement, purchased hours, consumed hours, remaining hours, expiry
  (if enabled), payment status, adjustments, and a complete transaction/usage ledger.
- Define exactly which events consume hours. The default business rule is that a
  completed, attended, billable teaching session consumes the student's actual
  billable duration. A cancelled session or a student absence must not silently consume
  hours; make absence/cancellation charging configurable and auditable.
- Prevent negative balances and duplicate deductions. Use transactions and idempotency
  for hour consumption, refunds, adjustments, and payment callbacks.
- Keep money/payment records separate from the hour ledger. A payment success must not
  be inferred from a client-side screen.

The package model must support configurable package sizes and pricing. Do not hardcode
30 hours as the only package.

## B. Student account creation and credential issuance (admin-only)

Student access is provisioned by the institute. This is a strict product invariant:

- There is no student self-registration, public sign-up page, or `/register` route.
- Only an authenticated, authorized institution admin can create a student account.
  Teachers cannot create accounts unless the owner explicitly approves that permission.
- The admin creates the student profile, then provisions or activates the associated
  login account and issues credentials directly to the student.
- Prefer a secure invitation or one-time temporary password flow. Require the student
  to set a new password at first login when a temporary password is used. Never
  display or store plaintext passwords after issuance.
- Provide admin-controlled password reset, account activation/deactivation, and access
  suspension. A student may change their own password after signing in, but cannot
  create an account or change account ownership/role.
- Do not implement `PendingApproval` for student self-registration. If an account
  status is needed, use admin-controlled states such as `Active`, `Inactive`, or
  `Suspended`.
- Keep credential issuance and reset actions auditable without logging passwords, reset
  tokens, or other secrets.
- Student profile creation and authentication-account provisioning should be separate
  operations with clear validation and recovery if one succeeds and the other fails.

## C. Student onboarding and initial academic assessment

A new student is onboarded exclusively by an institution admin. Students cannot
self-register, create their own accounts, or access a public registration route. The
admin creates the student profile, records the student's school/class/grade, syllabus
or curriculum (including NCERT and Kerala State syllabus), academic year where
applicable, and relevant subjects.

The institute can identify learning needs in either or both of these ways:

1. **Diagnostic assessment:** An admin or authorized teacher creates a basic assessment
   using questions mapped to subjects, class/grade, syllabus, chapter/topic, and
   difficulty. Questions may be multiple choice, OMR-style single/multiple selection,
   true/false, short answer, or another supported question type. The student completes
   it on paper or digitally. For paper OMR, support answer-key entry or a verified
   marking workflow; do not claim automatic optical recognition unless an OCR/OMR
   integration has actually been implemented and tested.
2. **Student/parent-provided learning needs:** A student or parent may tell the admin
   which subjects or topics are difficult. Parents do not have a login under the
   current product decision; an admin may record information supplied by a parent.
   Students can select or request weak subjects in their own portal.

Assessment results are recommendations and evidence for a learning plan, not an
unquestionable label of a student's ability. Admins/teachers can review and override
recommendations with an audit trail. Allow the institute to record the student's
self-reported weak subjects even when no diagnostic test is taken.

The outcome is a student learning profile containing syllabus, grade, subjects,
diagnostic attempts/results, identified weak areas, goals, and teacher/admin notes.

## D. Teacher preference and assignment

After onboarding and package/payment confirmation, teacher assignment can happen in
either of these ways:

- The student requests or selects a preferred teacher, subject to availability and
  institute approval.
- The admin assigns a teacher based on subject expertise, syllabus, availability,
  capacity, and the student's learning needs.

A student may work with different teachers for different subjects. Do not model a
single permanent teacher as a mandatory student property. Maintain explicit student-
teacher-subject assignments with effective dates, status, and assignment history.
A preferred teacher request is not a confirmed assignment until approved.

## E. Scheduling, sessions, and attendance

Admins and authorized teachers can schedule individual or group sessions. A session
may cover one or multiple configured subjects/topics only if the domain model supports
that explicitly. Each session must record its teacher, date/time, duration, delivery
mode/location or meeting details, subject/topic, participants, status, and relevant
package-hour rules.

Support the existing teaching modes: batch teaching, ad hoc group teaching, private
group teaching, and one-to-one teaching. A batch is optional; a one-to-one or ad hoc
session must not require a fake batch.

- Teachers need a daily/weekly timetable and a clear list of today's classes.
- Teachers can record attendance for each participant after or during the session,
  subject to the institute's attendance policy.
- Track attendance status (present, absent, late, excused, or institute-configured
  alternatives), marked-by, marked-at, and corrections.
- Prevent duplicate attendance records for the same participant/session.
- Admins can review and correct attendance with an audit trail.
- Rescheduling, cancellation, teacher substitution, and participant changes must update
  relevant views and preserve history.
- Handle time zones and recurring schedules safely.

## F. Class completion and learning record

After a class, the teacher can record a structured class report containing:

- What was taught and learning objectives.
- Subject, chapter, topic, and syllabus mapping.
- Notes and explanations.
- Homework or follow-up instructions.
- Reference video links or uploaded learning videos.
- Attachments such as PDF, PPT/PPTX, DOC/DOCX, images, and other approved formats.
- Teacher observations, student understanding, and recommended next steps.

Teachers can save drafts and publish when ready. Students can access only material
published for them or their assigned group. Keep a chronological class history for
each student and subject.

## G. Assignments, question bank, and examinations

Provide a reusable question bank and assessment builder. Authorized teachers/admins
can create questions manually or from approved uploaded materials.

Question and assessment content should support, as appropriate:

- Multiple choice and multiple select.
- True/false.
- Short and long written answers.
- Numerical answers.
- Image-based questions, diagrams, and question attachments.
- Questions that include uploaded images, tables, equations, or other learning media.
- Teacher-provided answer keys, marks, explanations, and rubrics.

Assignments and exams are distinct entities with their own due dates, availability
windows, marks, instructions, attempts, and submission rules.

For timed exams:
- Record start time, deadline, duration, attempt number, and submission time on the
  server.
- Enforce the timer and exam availability on the server, not only with a browser timer.
- Save answers progressively where practical and handle reconnects safely.
- Auto-submit or close an attempt when time expires according to exam settings.
- Prevent unauthorized changes to questions/answers after an attempt begins.
- Preserve the submitted answer snapshot even if the teacher later edits the source
  question.
- Record objective marks automatically only where a reliable answer key exists.
  Subjective answers require teacher review unless a clearly governed AI-assisted
  marking workflow is enabled.
- AI feedback/marking is advisory until reviewed by the teacher.

Students can view their own submissions, marks, teacher feedback, and allowed
corrections. Teachers can review answers, annotate or comment, award marks, and track
attempts. Students must never see another student's answers or results.

## H. Student books and AI-assisted chapter organisation

At onboarding, students may provide their own curriculum books or study resources.
Authorized teachers/admins can upload files such as PDF, PPT/PPTX, DOC/DOCX, and
supported image formats to the student's learning resources.

The platform should provide a document-processing pipeline that can, where technically
possible:

1. Validate file type, size, ownership, and access rights.
2. Store the original file unchanged.
3. Extract text and page structure from supported documents.
4. Use OCR for scanned/image-based PDFs when required.
5. Detect likely chapter, unit, lesson, and section boundaries.
6. Propose a chapter-wise table of contents and page ranges.
7. Create searchable extracted text and chapter-level references.
8. Allow a teacher to review, rename, merge, split, reorder, and correct detected
   chapters before publishing them.
9. Preserve links from each generated chapter back to the original file and page range.
10. Reprocess safely when a file is replaced, without creating duplicate chapters.

AI extraction is a draft-assistance feature, not an authority. Do not silently rewrite,
omit, or fabricate textbook content. Clearly show extraction confidence or processing
warnings where available. Keep the original source available for verification.

Support different syllabi and textbook structures, including NCERT and Kerala State
syllabus, without assuming all books share the same chapter format. Store curriculum,
class/grade, subject, book/edition, language (where known), and academic year metadata.

Do not send student documents or identifiable student information to an external AI
provider without the institute's configured provider, privacy review, and required
consent. Keep AI/document processing behind replaceable interfaces and background jobs.

## I. Teacher portal — student 360-degree view

A teacher should see only students they are authorized to teach or otherwise assigned
to support. The student profile should provide a coherent view of:

- Basic student details and contact information permitted for that teacher.
- Grade/class, curriculum/syllabus, academic year, and enrolled subjects.
- Weak subjects/topics and diagnostic assessment history.
- Current package, total/purchased/consumed/remaining hours, and relevant session
  ledger details (according to permission).
- Assigned teachers and assignment history where relevant.
- Upcoming schedule, completed sessions, cancellations, and attendance.
- Subject-wise learning progress and class reports.
- Published notes, videos, books, and chapter-wise materials.
- Assignments, due dates, attempts, submitted files/answers, marks, and feedback.
- Exams, timed attempts, answer scripts, results, and progress over time.
- Teacher observations and next recommended learning actions.

Provide filters and useful summaries, but do not expose financial or sensitive
administrative information beyond the teacher's permissions. Admins have the broader
institute-wide view and can manage access.

## J. Dashboards and reporting

Admin dashboard should help operate the institute: newly created student accounts,
credential issuance status, new student onboarding, diagnostic assessments awaiting
review, package/payment status, hour balances, teacher allocation, today's timetable,
attendance exceptions, upcoming exams, submissions awaiting marking, and operational
reports.

Teacher dashboard should prioritise today's timetable, assigned students, attendance
to complete, class reports to publish, submissions to mark, and upcoming deadlines.

Student dashboard should prioritise remaining learning hours, upcoming classes,
assigned teachers, weak-subject learning plan, recent notes/resources, assignments,
exams, results, and progress.

Do not use misleading progress percentages. Define how each metric is calculated and
show the underlying period or denominator.

## K. Core domain entities to consider

Audit the existing schema before adding entities. Reuse or extend existing equivalents
where appropriate. Likely concepts include:

- StudentProfile, Curriculum/Syllabus, Grade/Class, Subject, Chapter, Topic.
- DiagnosticAssessment, Question, QuestionOption, AssessmentAttempt, AssessmentAnswer,
  DiagnosticResult, StudentLearningNeed.
- LearningPackage, PackagePurchase, PaymentTransaction, StudentHourWallet or
  HourAccount, HourLedgerEntry, HourAdjustment.
- StudentTeacherSubjectAssignment, TeacherSubjectCapability, TeacherAvailability.
- TeachingSession, SessionParticipant, SessionAttendance, SessionClassReport.
- LearningResource, OriginalDocument, DocumentProcessingJob, ExtractedChapter,
  ChapterPageRange, ResourcePublication.
- QuestionBankItem, Assignment, AssignmentQuestion, AssignmentSubmission,
  Exam, ExamAttempt, ExamAnswer, MarkingRubric, TeacherFeedback.
- StudentProgressRecord, LearningGoal, AuditLog.

These are conceptual names, not an instruction to create every table. Inspect the
current domain first, avoid duplicate concepts, and design normalized relationships
with safe migrations.

## L. Role and access rules

The current approved sign-in roles are institution admin, teacher, and student.
Parents do not sign in. Parent-provided information can be recorded by an authorized
admin. Do not add parent authentication or a SaaS super-admin portal unless the owner
explicitly changes this decision.

This is one institute. Retain existing tenant/institution scoping and isolation if it
already exists, but do not build new multi-institution onboarding, billing, or
super-admin workflows.

## M. Suggested implementation sequence

Implement in complete, testable vertical slices:

1. Audit existing repository, schema, screens, APIs, and current flows.
2. Student academic profile: grade, syllabus, subjects, weak areas, and onboarding.
3. Diagnostic assessment builder, student attempt, marking, and result review.
4. Hour packages, payment records, hour ledger, and balance display.
5. Teacher preference/request and admin teacher assignment.
6. Teacher availability, timetable, session scheduling, participants, and attendance.
7. Class reports, notes, resource publishing, and reference videos.
8. Assignment creation, student submission, and teacher marking.
9. Exam builder, timed attempts, autosave, server-side expiry, and results.
10. Student book uploads, document processing, chapter extraction, and teacher review.
11. Teacher student-360 view, student progress, and admin reports.
12. Hardening: security, audit, accessibility, performance, backup/retention, and
    end-to-end regression tests.

Do not implement every module in one oversized change. Each slice must include its
database/API/UI permissions, validation, tests, and documentation.

## N. Required acceptance tests for these workflows

At minimum, verify:

- A student can have multiple weak subjects and multiple curriculum subjects.
- A diagnostic test can be created by an authorized user, attempted, marked, and
  reviewed without exposing answers prematurely.
- A student can request a preferred teacher and an admin can assign another teacher.
- Different subjects can have different assigned teachers.
- A package can be used across subjects without creating a separate course purchase.
- A completed billable session consumes the correct hours exactly once.
- A cancelled/non-billable session does not accidentally consume hours.
- Concurrent session completion/payment callbacks cannot double-deduct hours.
- A teacher can see only their authorized students and their permitted records.
- Attendance is unique per participant/session and corrections are auditable.
- A teacher can publish a class report with notes, attachments, and reference videos.
- Students can submit assignment files/answers and teachers can review them.
- Exam time is enforced server-side, including refresh/reconnect scenarios.
- The submitted exam answer snapshot remains unchanged if the question bank item changes.
- Original books remain available and chapter extraction can be reviewed/corrected.
- Scanned PDFs are identified as requiring OCR rather than being treated as empty text.
- Students cannot access another student's files, submissions, attendance, or results.

## O. Mandatory Claude Code skills — setup, selection, and enforcement

Skills are a required part of the Student Sync development workflow. They are not
optional suggestions. Claude Code must create, maintain, and use the relevant
project-level skills for implementation, design, testing, security, and review.

### O1. Required skill setup

At the beginning of the project setup (and whenever this section is updated):

1. Inspect the repository for `.claude/skills/`, existing `SKILL.md` files, and any
   globally installed or marketplace-provided skills. Do not overwrite existing
   skills without reviewing them.
2. Create the missing Student Sync project skills listed below under
   `.claude/skills/<skill-name>/SKILL.md`.
3. Ensure every skill has valid, clear Claude Code skill instructions and a useful
   description/frontmatter where supported by the installed Claude Code version.
4. Add any supporting templates, checklists, or reference documents inside that
   skill's own folder when they materially help its workflow.
5. Verify the resulting folder structure and read each created/updated `SKILL.md`
   before considering setup complete.
6. Keep the skills in the repository so they are version-controlled and available
   to every developer working on Student Sync.
7. Report which skills were created, already existed, updated, or could not be
   verified. Never claim a skill is installed merely because its name appears here.

Do not stop after writing this list in `CLAUDE.md`: the actual skill folders and
`SKILL.md` files must be created in the repository as a setup task.

### O2. Mandatory project skill catalogue

**Product and domain skills**

- `student-sync-domain`: institute workflow, domain terminology, business rules,
  entities, relationships, and product invariants.
- `student-onboarding-assessment`: admin-created student profiles, curriculum,
  diagnostic tests, attempts, marking, and learning needs.
- `hour-package-ledger`: packages, payments, entitlements, balances, hour deduction,
  refunds/adjustments, transaction history, and idempotency.
- `teacher-allocation-scheduling`: teacher matching, subject allocation, availability,
  timetable, time zones, sessions, and rescheduling.
- `attendance-session-management`: session participants, attendance statuses,
  session completion, billing eligibility, corrections, and audit history.
- `class-learning-content`: class reports, notes, videos, attachments, access rules,
  and publication workflows.
- `assignments-exams`: question banks, assignments, submissions, timed exams,
  answer snapshots, marking, and feedback.
- `document-chapter-processing`: secure document uploads, extraction, OCR, AI chapter
  detection, page mapping, review, and publication.
- `student-progress-360`: student learning history, progress, teacher observations,
  dashboards, and role-scoped reporting.

**UI, UX, and frontend skills — mandatory for interface work**

- `frontend-design`: plan and implement high-quality production frontend experiences.
  Define page structure, visual hierarchy, layout, typography, spacing, component
  composition, responsive behaviour, and interaction states. Avoid generic,
  repetitive AI-looking layouts and do not invent a competing Student Sync brand.
- `ui-ux-design-system`: maintain Student Sync design tokens, colours, typography,
  spacing, radii, elevation, iconography, reusable components, navigation patterns,
  form behaviour, accessibility, and design consistency. Inspect existing design
  references and `DESIGN.md` before proposing changes.
- `responsive-ui-implementation`: implement and verify desktop, tablet, and mobile
  layouts; handle breakpoints, overflow, touch targets, tables, dialogs, and navigation.
- `accessibility-audit`: check semantic structure, keyboard navigation, focus order,
  labels, contrast, accessible names, validation feedback, and screen-reader basics.
- `frontend-performance`: review rendering performance, bundle size, unnecessary
  re-renders, image/media loading, lazy loading, and client-side responsiveness.

**Browser automation and quality skills — mandatory for browser-verifiable work**

- `puppeteer-browser-testing`: use Puppeteer to run real-browser end-to-end checks,
  navigate the application, interact with forms and controls, verify role-based
  journeys, capture screenshots, inspect console errors, and report failed network
  requests. This is the required browser automation skill for Student Sync unless
  the repository has a documented technical incompatibility; any exception must be
  explained and approved before substituting another tool.
- `visual-regression-qa`: capture comparable screenshots at agreed viewports, inspect
  visual differences, and verify that UI changes do not unintentionally break existing
  screens. Use an existing visual testing system if present; do not add a duplicate
  service unnecessarily.
- `student-sync-testing`: unit, integration, API, database, component, browser,
  regression, and end-to-end test strategy for complete institute workflows.
- `student-sync-security`: authentication, authorization, tenant/data isolation,
  privacy, child safety, secure file access, audit logs, and threat-focused reviews.
- `code-review-refactoring`: review diffs for correctness, maintainability, security,
  performance, regressions, and unnecessary complexity before completion.
- `api-integration`: API contracts, validation, error handling, authentication,
  pagination, retries, idempotency, and frontend/backend integration.

### O3. Skill invocation rules — must follow

Before starting a task, Claude Code must identify and use the relevant skills:

- Any UI/page/component/visual change: use `frontend-design`,
  `ui-ux-design-system`, and `responsive-ui-implementation`.
- Any browser-visible workflow or frontend feature: also use
  `puppeteer-browser-testing` and `student-sync-testing`.
- Any visual redesign or styling change: also use `visual-regression-qa`.
- Any accessibility-sensitive UI or form: use `accessibility-audit`.
- Any API or frontend/backend integration: use `api-integration`.
- Any domain/business workflow: use the corresponding Student Sync domain skill.
- Any authentication, permissions, student data, files, or sensitive records: use
  `student-sync-security`.
- Any performance-related task: use `frontend-performance` where applicable.
- Before completing a substantial change: use `code-review-refactoring` and the
  relevant testing skills.

Do not merely mention the skill names in a plan. Read and follow the actual
`.claude/skills/<skill-name>/SKILL.md` instructions before doing the relevant work.
If a required skill is missing, create it using this catalogue before proceeding with
the task, then read and apply it. If a skill cannot be created or loaded, stop and
explain the blocker instead of silently skipping it.

For every substantial task, state briefly which skills will be used. In the completion
report, list the skills actually read and applied. Do not claim a skill was used if
its instructions were not read and followed.

### O4. Puppeteer setup and usage requirements

Puppeteer is the required browser automation path for Student Sync:

1. Inspect the repository and environment for existing Puppeteer setup, Node.js
   tooling, browser binaries, and existing browser test conventions.
2. Create `.claude/skills/puppeteer-browser-testing/SKILL.md` with the project's
   actual commands, configuration, authentication approach, test data rules, and
   screenshot locations.
3. If Puppeteer is not already available, check Node.js/package-manager versions,
   project structure, package compatibility, and security/maintenance status. Add it
   as a development-only test dependency in the appropriate test tooling location.
   Do not add it to production runtime dependencies.
4. Configure repeatable browser tests using fictional development data and safe test
   accounts. Never place real student data or secrets in tests or screenshots.
5. Use Puppeteer for important browser workflows: sign-in, role-based navigation,
   student creation, teacher allocation, scheduling, attendance, assignments, exams,
   document/chapter workflows, and relevant responsive screens as those features
   become available.
6. For UI changes, run the relevant Puppeteer checks and capture screenshots at
   desktop and mobile viewport sizes; include tablet where the layout warrants it.
7. Check browser console errors, failed requests, broken links, inaccessible controls,
   and visible loading/error/empty/success states.
8. Keep browser tests deterministic, isolated, and repeatable. Clean up test data safely.
9. If Puppeteer cannot run because of a verified platform limitation, report the exact
   reason and ask before using Playwright or another browser tool as a replacement.

Puppeteer browser checks do not replace unit tests, integration tests, API tests,
authorization tests, or manual review of sensitive workflows.

### O5. Skill file requirements

Each `SKILL.md` must contain:

- A concise purpose and explicit triggers for when Claude must use it.
- Required repository files, product documentation, and existing implementation to
  inspect before making changes.
- The applicable Student Sync business rules and invariants.
- A practical step-by-step workflow.
- Relevant architecture, security, privacy, and permission requirements.
- Required tests, browser checks, screenshots, or review steps.
- Acceptance criteria and a completion/reporting checklist.
- Links to the master prompt, `CLAUDE.md`, and `DESIGN.md` where relevant, rather
  than copying large sections of those documents.

Keep skills focused and complementary. Avoid duplicating the entire product
specification in every skill. Update a skill when the implementation or workflow
changes.

### O6. Claude Code operating practices

- Use Plan Mode for cross-module changes, substantial UI work, and schema-affecting
  tasks.
- Use Explore/read-only inspection before editing unfamiliar modules.
- Use focused subagents for independent audits or test reviews only when available;
  the primary agent remains responsible for integration and correctness.
- Use hooks for deterministic local quality checks such as formatting, linting, and
  secret detection; hooks are not a replacement for application security.
- Use MCP tools only when configured and authorized.
- Verify all skills, plugins, MCP servers, browser tools, and dependencies before
  claiming availability.
- Review the complete Git diff and run relevant tests before declaring completion.

The master prompt remains the detailed source of product requirements. This
`CLAUDE.md` defines the permanent engineering rules and mandatory skill usage.
The authoritative institute workflow addendum and explicit owner decisions take
precedence over older conflicting product wording.
