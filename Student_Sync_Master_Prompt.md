# STUDENT SYNC — MASTER PRODUCT, ARCHITECTURE, DESIGN & IMPLEMENTATION PROMPT
**Product:** Student Sync  
**Tagline:** LEARN • CONNECT • GROW  
**Positioning:** Your Complete Online Tuition & Learning Platform  
**Purpose:** Build a premium, secure, global, multi-tenant tuition and learning SaaS platform with web, PWA, and native Flutter experiences.

---

## 0. MASTER INSTRUCTION TO THE DEVELOPMENT AGENT

Act as the lead product architect, senior full-stack engineer, database architect, UI/UX designer, accessibility specialist, QA engineer, and technical reviewer for **Student Sync**.

Work inside the existing repository. First inspect the repository, its framework versions, conventions, current features, environment configuration, database state, and available tools. Preserve working functionality. Do not assume the repository is empty and do not overwrite existing work without understanding it.

Build a real, maintainable product—not a static mock-up, prototype full of fake buttons, or disconnected collection of screens.

### Non-negotiable engineering principles

1. **Correctness before speed.** Validate requirements, data relationships, permissions, migrations, and workflows before declaring completion.
2. **One source of truth.** Centralise business rules in the backend. Web, PWA, and Flutter clients must use the same APIs and authorization rules.
3. **Reusable by default.** Prefer shared components, widgets, services, validators, design tokens, and domain logic. Do not copy-paste similar implementations across screens.
4. **Database integrity is mandatory.** No duplicate primary IDs, orphan records, accidental cross-tenant references, invalid foreign keys, or silent data loss.
5. **No fake functionality.** Every visible action must work, be explicitly disabled with an explanation, or be clearly labelled as not yet available.
6. **No production hard-coded data.** Use database-backed APIs. Seed data is allowed only through controlled development/demo seeders.
7. **Security is enforced server-side.** Never rely on hidden UI elements or client-supplied tenant/user IDs as authorization.
8. **Test the rendered product.** Use Playwright and/or Puppeteer to interact with the running application, inspect screenshots, test workflows, and verify fixes.
9. **Accessibility and responsive design are part of implementation**, not optional polish.
10. **Do not claim success without evidence.** Report what was run, what passed, what failed, and what could not be tested.

### Work in controlled phases

Do not generate the entire application in one uncontrolled code dump. Begin with repository assessment, product architecture, data model, design system, and implementation plan. Then implement in small, coherent phases. Keep the application runnable at the end of each phase.

If a major decision is genuinely blocked by missing information, ask one concise question and state the default you recommend. Otherwise make a documented, reversible engineering decision and continue.

---

# PART I — PRODUCT VISION & BRAND

## 1. Product vision

Student Sync connects the full learning journey:

**Institution → Courses → Subjects → Batches or Individual Enrolments → Teachers → Students → Learning Sessions → Recordings → Notes → Assignments → Doubts → Assessments → Progress → Parents/Guardians.**

The product must support:
- Tuition centres and academies.
- Independent tutors and one-to-one teaching.
- Small and large group tuition.
- Batch-based teaching.
- Online and hybrid learning.
- International students and teachers.
- Multiple institutions under one SaaS platform.
- Future growth from an initial 100+ students and 10+ teachers to thousands of users.

Do not treat Student Sync as only a school administration system. It is a connected tuition operations and learning ecosystem.

## 2. Brand identity

Use the approved Student Sync logo and supplied brand reference as the source of truth. Do not randomly redesign the logo or alter its proportions.

Logo variants:
1. Full logo: symbol, Student Sync lettering, and tagline.
2. App icon: symbol only.
3. Monochrome logo: single-colour version for light and dark surfaces.

Brand personality:
- Smart, friendly, global, reliable, innovative, professional, student-focused.

### Official colour tokens

| Token | Colour |
|---|---|
| Deep Navy | `#0B2854` |
| Royal Blue | `#1264D8` |
| Vibrant Orange | `#FF9F24` |
| White | `#FFFFFF` |
| Soft Background | `#F3F6FC` |
| Slate Grey | `#64748B` |
| Success | `#16A36A` |
| Warning | `#F59E0B` |
| Error | `#E5484D` |
| Information | `#0EA5E9` |
| Border | `#E2E8F0` |
| Light Blue | `#EAF2FF` |
| Light Orange | `#FFF4E5` |

Use orange as a restrained accent, not the dominant colour. Maintain consistent contrast and semantic meaning.

### Typography and visual system

- Headings: Plus Jakarta Sans.
- Body and dashboard content: Inter.
- Numbers and statistics: Inter.
- Use a consistent type scale, line-height, spacing scale, border radius, shadows, and grid.
- Base spacing scale: 4, 8, 12, 16, 24, 32, 48, 64 px.
- Card radius: approximately 16 px.
- Button radius: approximately 10 px.
- Use clean white surfaces, navy navigation, royal-blue primary actions, orange accents, restrained shadows, spacious layouts, and clear icons.
- Avoid generic AI-generated dashboard styling, excessive gradients, decorative clutter, inconsistent card layouts, and oversized headings.

Create a root-level `DESIGN.md` documenting brand purpose, tokens, typography, spacing, breakpoints, components, accessibility, motion, and layout rules. All screens must follow it. Keep equivalent design tokens in Blazor/CSS and Flutter.

---

# PART II — REQUIRED TECHNOLOGY & ARCHITECTURE

## 3. Target stack

Use the following stack unless the existing repository has a well-founded constraint that requires an explicit documented alternative.

### Backend
- C# and ASP.NET Core Web API on .NET 10 LTS.
- Entity Framework Core.
- Clean Architecture with pragmatic modular-monolith boundaries.
- REST APIs and Swagger/OpenAPI.
- FluentValidation or equivalent.
- ASP.NET Core Identity and standards-based OAuth2/OIDC/JWT authentication.
- SignalR for suitable real-time updates.

### Database
- Microsoft SQL Server 2022.
- Relational schema with explicit constraints, foreign keys, unique indexes, check constraints, and appropriate indexes.
- EF Core migrations, transactional writes, audit trails, and tested backup/restore procedures.

### Web
- Blazor Web App.
- Responsive desktop, tablet, and mobile layouts.
- Installable PWA capabilities where supported.
- Accessible semantic UI.

### Mobile
- Flutter and Dart.
- Genuine native Android and iOS apps—not a WebView wrapper.
- Feature-oriented architecture.
- Shared backend APIs and business rules.

### Background processing and storage
- Hangfire or an equivalent reliable background-job system.
- Azure Blob Storage or S3-compatible object storage for recordings and attachments.
- Signed, short-lived media URLs and authorization checks.
- Email/push providers behind abstractions.

### Delivery and operations
- Docker where appropriate.
- CI checks, automated tests, structured logs, health checks, metrics, monitoring, secrets management, and automated backups.
- Keep the architecture a modular monolith initially. Do not introduce microservices without a measured need.

## 4. Architecture boundaries

Use clear modules such as:
- Identity and Access
- Tenancy and Institutions
- People and Relationships
- Academic Catalogue
- Teaching Modes and Enrolments
- Scheduling and Calendar
- Learning Sessions
- Meetings and Integrations
- Attendance
- Learning Content and Notes
- Recordings and Transcripts
- Assignments and Submissions
- Assessments and Question Banks
- Doubt Centre
- Messaging and Announcements
- Notifications
- AI Class Assistant
- Progress and Reporting
- Fees and Payments
- Audit and Compliance

Keep domain logic out of UI components and controllers. Controllers/endpoints should be thin. Use application services/use cases, domain rules, repositories only where they add value, and explicit DTOs. Avoid an unnecessary abstraction for every class.

---

# PART III — TEACHING MODES: BATCH, GROUP & ONE-TO-ONE

## 5. This is a mandatory first-class product capability

Student Sync must support both **group/batch-based learning** and **one-to-one private tuition without requiring a batch or group**. Do not design the database, UI, scheduling, attendance, assignments, or permissions around the assumption that every student belongs to a batch.

### Supported teaching modes

1. **Batch-based group session**
   - A predefined batch has multiple enrolled students.
   - A teacher is assigned to the batch/course/subject.
   - Sessions are scheduled for the batch.
   - Attendance and shared resources are managed for the session.
   - Students see only sessions and materials they are entitled to access.

2. **Ad hoc group session**
   - A teacher or authorised administrator creates a group session for selected students without requiring a permanent batch.
   - The session has its own participant list and permissions.
   - It may optionally be linked to a course, subject, or batch.

3. **One-to-one private session**
   - One teacher and one student.
   - No batch or group record is required.
   - The session can be created directly from the teacher-student relationship, student profile, calendar, or booking workflow.
   - Private notes, attendance, recording, assignments, and AI recap are visible only to authorised participants.
   - Support recurring private lessons and one-off lessons.

4. **Small-group/private group**
   - A teacher teaches a selected set of students.
   - It can be recurring or one-off.
   - It does not need to be a formal batch.

5. **Hybrid or in-person session**
   - Support online meeting links, physical venue/location, or hybrid arrangements.
   - Store delivery mode and relevant access details securely.

### Data model implications

Create a flexible session model with a required `SessionType` or equivalent enum, for example:
- `Batch`
- `Group`
- `OneToOne`
- `PrivateGroup`

A session must not require a `BatchId`. `BatchId` is nullable and only populated when the session genuinely belongs to a batch. Use a dedicated session-participant/enrolment join table to represent teacher and student participation.

Suggested conceptual entities:
- `TeachingArrangements`
- `LearningSessions`
- `LearningSessionParticipants`
- `StudentTeacherRelationships` (if needed for permissions/history)
- `BatchEnrolments`
- `SessionRecurrences`
- `SessionLocations`
- `SessionResources`

Use explicit participant roles and statuses. Enforce valid combinations in the domain layer and database where practical. Do not create fake batch records to represent one-to-one tuition.

### UI and workflows

- Offer clear “Create Batch Class”, “Create Group Session”, and “Schedule One-to-One” actions.
- A teacher can select a student directly for one-to-one tuition.
- A teacher can select several students for an ad hoc group.
- An administrator can create, assign, reschedule, or cancel any permitted session.
- Student dashboards show only their own sessions.
- Teacher dashboards show all assigned sessions.
- Parent dashboards show sessions for linked children only.
- Calendar filters must include teaching mode.
- Attendance must work for one student as well as many.
- Assignments, notes, doubts, recordings, and AI outputs must be linked to the learning session, not depend on a batch foreign key.
- Reports must distinguish batch, group, and one-to-one learning without double-counting.

### Acceptance tests

Verify:
- Create a batch with multiple students and schedule a session.
- Create a group session without a batch.
- Create a one-to-one session without any batch or group.
- Schedule recurring one-to-one lessons.
- Add/remove a participant with audit history and appropriate policy checks.
- Confirm unrelated students cannot access a private session.
- Confirm one-to-one attendance, notes, assignments, recordings, and AI recaps work.
- Confirm no business workflow fails when `BatchId` is null.

---

# PART IV — USERS, ROLES & PERMISSIONS

## 6. Roles

### SaaS Super Administrator
- Manage tenants/institutions, subscriptions, platform administrators, global settings, feature flags, integrations, usage, system health, and platform audit logs.
- No unrestricted access to tenant educational content by default. Any exceptional support access must be authorised, scoped, time-limited, and audited.

### Institution/Tuition Centre Administrator
- Manage institution profile, students, teachers, parents, courses, subjects, batches, enrolments, teaching arrangements, schedules, calendar, attendance, fees, reports, announcements, notifications, permissions, and settings.

### Teacher
- Manage assigned classes, students, subjects, batches, one-to-one learners, timetable, live sessions, attendance, notes, recordings, materials, homework, assignments, quizzes, examinations, grading, doubts, communication, and class preparation.
- Teachers must not access students or sessions outside their authorised scope.

### Student
- View own dashboard, sessions, meeting links, recordings, notes, materials, assignments, submissions, quizzes, exams, doubts, teacher messages, calendar, notifications, attendance, results, progress, bookmarks, and profile.

### Parent/Guardian
- Link to one or more children with verified relationships and scoped permissions.
- View permitted attendance, progress, assignments, upcoming/missed classes, results, feedback, reports, and notifications.
- No access to private teacher/student communications unless explicitly allowed by policy.

### Permissions
Implement role-based access control plus fine-grained permissions and resource-level authorization. Enforce authorization in the backend on every request. Test horizontal and vertical privilege escalation, tenant boundaries, participant access, and parent-child relationships.

---

# PART V — CORE PRODUCT MODULES

## 7. Dashboards

### Administrator dashboard
- Student and teacher counts.
- Active courses and batches.
- Upcoming sessions.
- Attendance overview.
- Outstanding tasks.
- Institution activity.
- Fee summaries where enabled.
- Quick actions and alerts.

### Teacher dashboard
- Today's classes and next class.
- Batch and private one-to-one sessions.
- Upcoming timetable.
- Pending attendance.
- My students.
- Unresolved doubts.
- Assignments to grade.
- Recent submissions.
- Pending AI note approvals.
- Class preparation and deadlines.
- Quick actions.

### Student dashboard
- Welcome message.
- Next-class countdown.
- Upcoming session and prominent Join button.
- My Learning Hub.
- Recorded lessons, study notes, assignments, Ask Doubt.
- Upcoming classes, recent activity, unanswered doubts, learning progress, notifications.
- Clearly distinguish batch sessions from private sessions where useful.

### Parent dashboard
- Child switcher for multiple children.
- Upcoming classes.
- Attendance, assignments, results, progress, teacher feedback, weekly reports, and notifications.

Each dashboard must be data-backed, permission-aware, responsive, and useful—not just a grid of statistics.

## 8. Academic management
Support:
- Academic years.
- Courses and course versions where useful.
- Subjects.
- Chapters/topics.
- Batches and batch capacity.
- Teacher assignments.
- Student enrolments.
- One-to-one teacher/student arrangements.
- Group participant lists.
- Course materials and academic history.
- Multiple courses, subjects, teachers, and teaching arrangements per student.

## 9. Scheduling, calendar and time zones

This is critical for global users.

- Store event instants in UTC.
- Store IANA time-zone identifiers for scheduling context and user preferences.
- Convert display times for each user.
- Use time-zone-aware recurrence rules; never use fixed UTC offsets for recurring sessions.
- Handle daylight saving transitions, local date changes, ambiguous/nonexistent local times, and cancellations.
- Support teacher and student time-zone preferences, manual selection, conflict detection, recurring sessions, rescheduling, cancellation, holidays, calendar export, and Google Calendar sync.
- Clearly show the time zone when creating or editing a schedule.
- Detect teacher/student conflicts and explain them before saving.
- Preserve original schedule and audit history when a session is rescheduled.

Example: a teacher schedules 7:00 PM Dubai time. Students in London, India, and New York must see the correct local date and time for that specific occurrence.

## 10. Learning Session Hub

Every learning session—batch, group, one-to-one, online, in-person, or hybrid—has one dedicated hub.

Include:
- Session details: title, type, subject, chapter, teacher, participants, schedule, objectives, location/meeting link, status.
- Live class: join link, host details, attendance, meeting state.
- Notes: rich text, headings, images, tables, maths/formulas, attachments, key points, homework.
- Recordings: secure player, duration, chapters, bookmarks, resume playback, speed controls, progress.
- Materials: files, links, worksheets, slides, and resources.
- Assignments: linked homework, deadlines, submissions, feedback.
- Doubts: session-linked questions, attachments, replies, status.
- Assessments: quizzes and understanding checks.
- AI recap and transcript, if enabled and approved.

All related records should reference the session. Batch linkage is optional.

## 11. Google Meet and calendar integrations

Use official supported Google APIs and OAuth flows.
- Connect Google accounts with least-privilege scopes.
- Create or associate meeting spaces/links where supported.
- Schedule sessions and assign hosts.
- Join through web and Flutter.
- Synchronise calendar details where authorised.
- Retrieve attendance, recordings, transcripts, and smart notes only when the account, licence, administrator configuration, API, and permissions support it.
- Show honest capability/status messages; never imply a recording exists when it does not.
- Provide manual meeting link entry, recording upload, transcript upload, and notes entry as fallbacks.
- Store integration tokens securely and support revocation/reconnection.
- Handle API quotas, retries, webhook verification, and sync idempotency.

## 12. Doubt Centre — signature feature

Students can:
- Ask questions with text, image, PDF, and voice attachments.
- Select subject/chapter and link to a session or recording timestamp.
- Ask private questions where permitted.
- Track status and confirm resolution.

Statuses:
`New`, `Assigned`, `InProgress`, `Answered`, `PendingConfirmation`, `Resolved`, `Reopened`.

Teachers can:
- Filter and assign doubts.
- Reply with explanations and attachments.
- Set priority.
- Schedule a doubt for the next relevant class.
- Mark it discussed.
- Track resolution.

### Automatic unresolved-doubt follow-up
1. Find unresolved doubts in the correct student/subject/teaching context.
2. Associate them with the next relevant session when appropriate.
3. Show them in teacher class preparation.
4. Notify the student and teacher according to preferences.
5. Add the doubt to a draft agenda.
6. Let the teacher discuss it.
7. Ask the student to confirm resolution.
8. Keep it open or reopen it if unresolved.

Prevent duplicate reminders and preserve the full history. This must work for one-to-one tuition without a batch.

## 13. Recordings and learning library
- Upload authorised recordings or synchronise available meeting recordings.
- Organise by institution, course, subject, chapter, session, and permitted cohort/participants.
- Secure playback via authorised, short-lived URLs.
- Search/filter, thumbnails, resume, playback speed, bookmarks, timestamp notes, recently watched, completion, and teacher recommendations.
- Do not expose public storage URLs or allow access by guessing identifiers.

## 14. Assignments and homework
Support written answers, MCQs, PDF/image/document uploads, practical work, voice submissions, and online quizzes.

Teachers:
- Create, schedule, assign to batch/group/selected students/one-to-one student.
- Set deadline, resources, late policy, grading rubric, marks, feedback, correction requests, and resubmission policy.

Students:
- View instructions, save drafts, submit answers/files, track status, and view marks/feedback.
- Enforce ownership and deadline policy on the backend.

## 15. Examinations and question banks
Question types:
- Single choice, multiple choice, true/false, short answer, long answer, fill blanks, matching, numerical.

Support:
- Timed exams, randomised questions, difficulty levels, attempts, autosave, automatic/manual grading, results, and topic-level performance.
- Protect answer keys and exam content from unauthorised access.
- Handle interrupted attempts and idempotent submissions.

## 16. Communication and notifications
- Private teacher-student conversations, batch/course announcements, group messages where authorised, file attachments, read status, and unread counts.
- SignalR for appropriate live updates.
- Notification channels: in-app, web push, Flutter push, email, and optional WhatsApp integration.
- Events: upcoming classes, changes, cancellations, recordings, notes, assignments, deadlines, doubt replies/follow-ups, exams, announcements, attendance, and AI review tasks.
- Respect user preferences, quiet hours, time zones, consent, and delivery status.
- Use idempotency/deduplication keys to prevent duplicate notifications.

## 17. Academic progress
Track meaningful evidence:
- Attendance.
- Lessons completed and recordings watched.
- Assignments and submissions.
- Quiz/exam outcomes.
- Topic understanding where measured.
- Unresolved doubts.
- Learning consistency.

Avoid arbitrary or misleading scores. Explain how any metric is calculated. Do not use recording metadata to rank or evaluate students.

## 18. Additional differentiators
- Personal Learning Timeline.
- Teacher Class Preparation showing previous doubts, missed lessons, and pending homework.
- Timestamp-based video doubts.
- Personal revision planner.
- Weekly parent digest.
- English, Arabic, and Malayalam initially, expandable.
- Arabic right-to-left layouts.
- Study streaks that encourage without unfairly penalising.
- Smart Class Recap.
- Offline access to permitted notes/materials.
- Learning recommendations linked to actual course content.

## 19. Financial management (optional module)
Institution-controlled fee plans, course/monthly fees, payment records, partial payments, outstanding balances, discounts, receipts, refunds, and reports. Use a proper payment-provider integration for actual payments. Never store raw card details. If this module is not configured, do not show fake payment functionality.

---

# PART VI — STUDENT SYNC AI CLASS ASSISTANT

## 20. Purpose

Create a first-class module that helps teachers capture authorised lessons, understand the discussion, generate structured educational drafts, and help students revise.

Workflow:
**Authorised live class → recording/transcript artifacts → transcription/processing → structured educational draft → teacher review → approval → student publication.**

Do not assume every Google account supports recording, transcripts, or smart notes. Use the available authorised artifacts and provide manual upload/entry fallbacks.

## 21. Consent, privacy and recording controls
- Clearly disclose recording/transcription.
- Capture and retain consent records where required.
- Allow institution/teacher controls for recording, transcription, or both.
- Respect local law, child-protection requirements, Workspace policies, and retention schedules.
- Recording must not begin without the required permissions and consent.
- Provide deletion and retention workflows.
- Apply strict access controls to recordings and transcripts.

## 22. Transcription
- Retrieve authorised Google transcripts where available.
- If only audio is available and processing is authorised, use a configured transcription provider.
- Preserve timestamps and speaker attribution where supported.
- Support multiple languages and accents.
- Preserve mathematical notation and subject vocabulary as accurately as possible.
- Flag low-confidence sections.
- Keep original transcript immutable and store teacher-corrected versions separately.
- Allow teachers to review/correct transcript text.

## 23. Lesson understanding
Extract only evidence-supported information:
- Subject, chapter, topics, subtopics.
- Learning objectives.
- Definitions and formulas.
- Teacher explanations.
- Worked examples.
- Student questions and teacher answers.
- Unanswered questions.
- Homework and assignment instructions.
- Explicit deadlines only when stated.
- Revision topics and next-session topics.

Never fabricate lesson content, formulas, homework, deadlines, or speaker attribution. Distinguish what was actually discussed from additional AI recommendations. Cite timestamps/source segments where available.

## 24. Generated outputs
For each session, create drafts for:
1. Quick recap and learning outcomes.
2. Detailed topic-wise notes.
3. Definitions and formulas.
4. Step-by-step examples.
5. Questions asked and answers given.
6. Unanswered questions.
7. Homework extraction with confidence/source evidence.
8. Revision summary.
9. MCQs and short-answer practice questions with explanations.
10. Flashcards.
11. Suggested next-class agenda.
12. Relevant timestamp/chapter links.

Mark suggestions as AI-generated recommendations, not confirmed teacher instructions.

## 25. Teacher approval workflow
Statuses:
`RecordingPending`, `TranscriptionPending`, `AIProcessing`, `DraftGenerated`, `AwaitingTeacherReview`, `Approved`, `Published`, `ProcessingFailed`.

Teacher can preview, edit, correct, remove, add, regenerate selected sections, approve, publish, or reject drafts. Maintain version history and audit records. Do not publish AI-generated educational notes until approved, unless an institution explicitly configures a lawful and appropriate alternative policy.

## 26. Student AI learning assistant
- Answer using approved materials the student is authorised to access.
- Retrieve relevant notes/transcript segments and timestamps.
- Link to the original recording where permitted.
- Clearly distinguish AI explanations from the teacher's original words.
- Do not pretend unsupported content was taught in class.
- Allow unanswered questions to be sent to the teacher.
- Enforce access control for every retrieval.

## 27. AI architecture and resilience
- Provider abstraction for transcription and language models.
- Configurable providers, models, quotas, usage limits, and cost tracking.
- Asynchronous jobs with retries, idempotency, status, logs, and failure handling.
- Manual notes and ordinary teaching workflows must continue if AI providers are unavailable.
- Validate structured model output before saving.
- Store source references and processing versions.
- Avoid sending unnecessary student personal data to AI providers.

Suggested entities:
`AIProcessingJobs`, `ClassRecordings`, `ClassTranscripts`, `TranscriptSegments`, `TranscriptSpeakers`, `AIGeneratedNotes`, `AIGeneratedTopics`, `AIGeneratedQuestions`, `AIGeneratedAssignments`, `AIGeneratedQuizzes`, `TeacherApprovals`, `AIUsageRecords`, `RecordingConsentRecords`.

Every record must be correctly scoped to tenant, session, course where applicable, teacher, and authorised participants. Batch is optional.

---

# PART VII — DATABASE QUALITY, IDENTIFIERS & SEED DATA

## 28. Database-first correctness requirements

Before implementing modules:
1. Produce an ERD and data dictionary.
2. Define ownership, lifecycle, nullability, cardinality, and delete/update behaviour for every relationship.
3. Identify tenant-scoped and globally unique entities.
4. Define unique constraints and natural-key rules.
5. Define concurrency and transaction strategy.
6. Create migration and rollback strategy.
7. Review indexes against actual query patterns.
8. Add integration tests against SQL Server (or a compatible test database strategy that does not conceal SQL Server-specific behaviour).

### Identifier rules
- Use a consistent primary-key strategy (for example, `uniqueidentifier`/GUID or a carefully managed bigint identity); do not mix strategies without reason.
- IDs must be generated by one authoritative layer and never manually reused.
- Never use display names, email addresses, or sequential client values as primary keys.
- Add unique constraints for tenant-scoped business identifiers where required.
- Use composite unique indexes where the business rule is tenant-scoped, such as `(TenantId, NormalizedEmail)` when appropriate.
- Protect against duplicate enrolments, duplicate session participants, duplicate attendance rows, duplicate processing jobs, duplicate webhook events, and duplicate notifications.
- Use idempotency keys for retried commands and integrations.
- Use optimistic concurrency (rowversion or equivalent) for mutable records where concurrent editing is possible.
- Use transactions for multi-record operations.
- Validate all foreign-key ownership and tenant consistency before saving.
- Never accept a foreign key merely because the ID exists; confirm it belongs to the same tenant and permitted context.
- Avoid cascade-delete configurations that could erase academic or financial history. Prefer soft deletion/archive where appropriate and explicit retention policies.
- Add audit fields such as `CreatedAtUtc`, `CreatedByUserId`, `UpdatedAtUtc`, `UpdatedByUserId`, and concurrency tokens where appropriate.
- Use UTC for instants and explicit date/time-zone fields for local scheduling intent.
- Use decimal types with explicit precision for money; never floating point.
- Store file metadata separately from file bytes; use object storage for large files.
- Protect sensitive fields and secrets; never log credentials, tokens, or private content unnecessarily.

### Required database validation
- Detect duplicate primary/business keys.
- Detect orphan foreign keys.
- Detect cross-tenant references.
- Detect duplicate join rows.
- Detect invalid enum/status transitions.
- Detect overlapping or conflicting schedules according to business rules.
- Detect duplicate or missing attendance records.
- Detect invalid session participant combinations.
- Detect batch-required assumptions in one-to-one flows.
- Test migration from an empty database and upgrade from the previous migration.
- Test backup and restore in a documented environment.
- Ensure seed scripts are repeatable and safe to run more than once.

## 29. Development/demo seed data

Seed useful, realistic, clearly fictional demo data so the application is immediately testable. Seed data must be created through EF Core seeding or a dedicated idempotent development seeder—not scattered hard-coded UI arrays.

Seed at least:
- One demo tenant/institution.
- One super-admin account (only if local development needs it).
- Two institution administrators if useful for permission tests.
- Several teachers.
- Several students.
- At least two parents/guardians with linked children.
- Multiple courses and subjects.
- Chapters/topics.
- One batch with multiple students.
- One ad hoc group session without a batch.
- At least two one-to-one teacher/student arrangements and sessions with `BatchId = null`.
- A recurring one-to-one session.
- Upcoming, completed, cancelled, and rescheduled sessions.
- Attendance records for group and individual sessions.
- Sample lesson notes/materials.
- A sample assignment with submission and feedback.
- A few doubts in different statuses, including one unresolved doubt linked to a future session.
- Sample notifications and announcements.
- Sample assessment/question-bank records.
- Sample AI class recap in draft and approved states, clearly labelled as demo content.
- Sample recording/transcript metadata only; do not invent a real recording URL.

### Seed data safety
- Use deterministic, stable IDs only within the development seed process, or generate IDs and resolve relationships safely.
- Enforce uniqueness and idempotency.
- Do not seed demo accounts into production.
- Do not commit real passwords or production secrets.
- Use environment variables or a local-only setup command to establish demo credentials. If a local demo password is needed, display it only in development documentation/terminal output and force change on first login.
- Use fictional names and non-deliverable example email domains such as `example.test`.
- Make seed data clearly identifiable as demo data.
- Include a command such as `dotnet run -- seed-demo` or a documented equivalent.
- Provide a reset script that only works in an explicitly confirmed development environment.
- Test that running the seeder twice does not create duplicates.

---

# PART VIII — REUSABLE COMPONENTS & WIDGET-FIRST IMPLEMENTATION

## 30. Global component strategy

The product must be widget/component-based. Build a reusable design system and shared component library so the same patterns are used consistently throughout the product.

### Shared web components
Examples:
- AppShell, Sidebar, TopBar, MobileNavigation.
- PageHeader, Breadcrumbs, Tabs.
- StatCard, SummaryCard, InfoCard.
- DataTable, DataGrid, Pagination, FilterBar.
- SearchBox, DatePicker, TimePicker, TimeZonePicker.
- FormField, TextInput, Select, MultiSelect, FileUploader.
- PrimaryButton, SecondaryButton, IconButton, SplitButton.
- StatusBadge, Avatar, AvatarGroup.
- Modal, Drawer, ConfirmDialog, Toast.
- EmptyState, ErrorState, LoadingSkeleton, RetryPanel.
- Calendar, SessionCard, ParticipantList.
- AttendancePanel, AssignmentCard, SubmissionCard.
- DoubtCard, ConversationPanel, MessageComposer.
- VideoPlayer, TranscriptViewer, TimestampLink.
- AIReviewPanel, ApprovalBar.
- PermissionGate and FeatureGate.

### Flutter shared widgets
Create equivalent reusable Flutter widgets and theme tokens. Keep interaction patterns consistent while respecting native platform conventions.

### Reuse rules
- Build components around stable design and behaviour contracts.
- Use composition and slots/child content instead of creating many near-identical components.
- Keep components accessible, typed, documented, and independently testable.
- Avoid one giant component with dozens of unrelated flags.
- Avoid creating abstractions for one-off trivial markup.
- Centralise shared validation, formatting, date/time conversion, permission checks, API clients, error handling, and design tokens.
- Do not duplicate business logic in web and mobile clients.
- Use generated API clients/contracts where practical.
- Create shared form schemas/validation rules where feasible.
- Use a single source for status labels, colour mappings, and enums.
- Prefer configuration-driven tables/forms for repeated CRUD patterns.
- Keep feature-specific components thin and compose them from shared primitives.
- Refactor repeated code when patterns become clear; do not perform risky rewrites without tests.

### Component acceptance criteria
- Components render correctly at supported breakpoints.
- Loading, empty, error, disabled, success, and permission-denied states are handled.
- Keyboard and screen-reader support is included.
- No inconsistent spacing, colours, typography, or button behaviour.
- Unit/widget tests cover important states.
- Shared changes are regression-tested across the screens that use them.

---

# PART IX — WEB, PWA & FLUTTER EXPERIENCE

## 31. Required web screens

### Administration
Login, dashboard, institution settings, students, teachers, parents, courses, subjects, batches, enrolments, teaching arrangements, timetable, calendar, live sessions, attendance, reports, fees (if enabled), notifications, permissions, and settings.

### Teacher
Dashboard, my classes, class details, session preparation, group/batch sessions, one-to-one sessions, Google Meet, attendance, lesson notes, recording management, AI recap review, assignments, grading, Doubt Centre, student profiles, calendar, communication, and availability.

### Student
Dashboard, my classes, one-to-one lessons, class details, live session, recorded lessons, video player, notes, assignments, submissions, Doubt Centre, calendar, progress, notifications, and profile.

### Parent
Dashboard, children, attendance, assignments, results, progress, calendar, reports, and notifications.

## 32. PWA
- Installable manifest and icons.
- Service worker and safe update mechanism.
- Responsive layouts.
- Offline fallback and connection status.
- Cache static assets safely.
- Cache only explicitly permitted offline notes/materials.
- Never cache sensitive data in shared/public caches.
- Push notifications where supported.
- Document browser/iOS-specific limitations.

## 33. Flutter
Native Android and iOS applications using the same backend:
- Secure authentication and token refresh.
- Deep links.
- Push notifications.
- Google Meet joining.
- Video playback.
- Notes and permitted offline materials.
- Homework and file attachments.
- Camera/gallery and voice questions with permission handling.
- Attendance for authorised teachers.
- Native navigation and platform conventions.

Suggested navigation:
- Student: Home, My Classes, Learning, Doubts, Profile.
- Teacher: Dashboard, Classes, Students, Doubts, Tasks, Profile.
- Parent: Home, Children, Calendar, Progress, Profile.

---

# PART X — SECURITY, PRIVACY & COMPLIANCE

## 34. Security requirements
- Secure authentication, password policy, MFA readiness, refresh-token rotation/revocation.
- Server-side role, permission, tenant, and resource authorization.
- Encryption in transit and appropriate encryption at rest.
- Input validation and output encoding.
- SQL injection protection through parameterised ORM/query practices.
- XSS, CSRF, CORS, SSRF, and open-redirect protections as applicable.
- Rate limiting and abuse protection.
- Secure upload validation, malware scanning strategy, file size/type limits, and private storage.
- Secrets in a secrets manager/environment—not source control.
- Audit logs for sensitive actions.
- Secure account recovery and session management.
- Data export, retention, and deletion workflows.
- Backup encryption and restore testing.

## 35. Multi-tenancy
- Tenant isolation must be enforced in application services/data access and authorization.
- Derive tenant context from the authenticated principal and validated membership, not from an untrusted request field.
- Validate tenant ownership for every related entity and file.
- Test cross-tenant access attempts for every module.
- Super-admin support access must be exceptional, auditable, and scoped.

## 36. Children’s data
Treat children’s educational data, recordings, transcripts, and parent relationships as highly sensitive. Follow applicable privacy and child-protection laws in each operating jurisdiction. Collect only necessary data. Apply consent, access, retention, and deletion controls. Do not use recording metadata to rank students.

---

# PART XI — RESEARCH, DESIGN & DEVELOPMENT SKILLS

## 37. Product research
Before committing to major UX decisions, research relevant products such as Google Classroom, Moodle, Canvas LMS, Classplus, Teachmint, and comparable tuition platforms.

Compare:
- Scheduling and calendar.
- Batch and private tuition workflows.
- Live and recorded classes.
- Notes and learning materials.
- Homework and assessments.
- Doubt workflows.
- Teacher preparation.
- Parent experience.
- International time zones.
- Mobile experience.
- Pricing/limitations where publicly available.

Create a sourced feature matrix. Separate verified findings from assumptions. Do not claim Student Sync is unique without evidence.

## 38. Required design and engineering skills/tools

Use available skills and tools in the environment. Do not pretend a skill or plugin is installed if it is not. If a required tool is missing, state the limitation and use the closest available alternative.

Prioritise these capabilities:

1. **Frontend design skill** — establish a deliberate visual direction and implement a cohesive design system before building screens.
2. **Playwright** — browser automation, real user flows, screenshots, responsive checks, console/network inspection, and end-to-end tests.
3. **Puppeteer** — use when available and useful for browser automation, screenshots, PDF/render checks, or workflows not covered by Playwright. Avoid maintaining two redundant test suites without a reason.
4. **Design review skill** — inspect rendered pages for hierarchy, spacing, alignment, typography, contrast, consistency, and responsive defects.
5. **Accessibility/WCAG audit** — semantic HTML, labels, alt text, keyboard access, focus order, ARIA, contrast, and touch targets.
6. **Visual regression** — maintain screenshot baselines and compare changes at key breakpoints.
7. **Web app testing/E2E** — verify complete user journeys, not only isolated components.
8. **Code review** — inspect architecture, security, performance, state management, duplication, and maintainability.
9. **Optional performance/Lighthouse audit** — assess performance, accessibility, SEO where relevant, and best practices.

If using Claude Code and the relevant official/community plugins are available, use the frontend-design and Playwright capabilities. A possible setup in a Claude Code environment is:

```bash
/plugin marketplace add anthropics/claude-plugins-official
/plugin install frontend-design@claude-plugins-official
/plugin install playwright@claude-plugins-official
```

An alternative Playwright MCP setup may be:

```bash
claude mcp add playwright -s user -- npx @playwright/mcp@latest
```

These are environment setup suggestions, not proof of installation. Verify availability in the current environment before relying on them. Add design-review, accessibility-audit, and visual-regression capabilities where available.

---

# PART XII — TESTING & VISUAL QUALITY LOOP

## 39. Mandatory browser workflow

After implementing a meaningful screen or workflow:

1. Start the application using the documented development command.
2. Confirm the server is healthy.
3. Open it with Playwright; use Puppeteer where appropriate/available.
4. Test at viewport widths:
   - Mobile: 375 px.
   - Tablet: 768 px.
   - Desktop: 1440 px.
5. Capture full-page screenshots and relevant interaction-state screenshots.
6. Inspect the actual rendered UI—not only source code.
7. Check layout, hierarchy, spacing, typography, colour, contrast, overflow, responsive navigation, and form states.
8. Test keyboard navigation and visible focus.
9. Inspect browser console errors and failed network requests.
10. Run accessibility checks, preferably axe-core or an equivalent tool.
11. Identify defects by severity.
12. Fix them in small changes.
13. Re-run affected tests and capture after-screenshots.
14. Compare against `DESIGN.md` and visual baselines.
15. Record remaining issues honestly.

Do not say “looks good” without inspecting screenshots or a rendered page when browser tools are available.

## 40. Automated testing

Create and maintain:
- Unit tests for domain rules and services.
- Integration tests for APIs and database behaviour.
- Migration tests.
- Authorization and tenant-isolation tests.
- Duplicate/idempotency tests.
- API contract tests.
- Time-zone and recurrence tests.
- One-to-one and batch/group workflow tests.
- Notification deduplication tests.
- AI pipeline state and approval tests.
- Flutter widget tests.
- PWA tests.
- Playwright E2E tests.
- Visual regression tests for key screens.

### Critical end-to-end journeys
1. Admin creates institution, teacher, student, course, subject, and batch.
2. Admin schedules a batch session; students see it in local time.
3. Teacher creates a group session without a batch and invites selected students.
4. Teacher schedules one-to-one tuition without creating a batch.
5. Teacher creates recurring one-to-one sessions; time-zone/DST behaviour is correct.
6. Student joins the authorised session and sees permitted materials.
7. Teacher records attendance for one-to-one and group sessions.
8. Student submits homework; teacher grades it.
9. Student asks a doubt; teacher responds; unresolved follow-up appears in the next relevant session.
10. Teacher uploads or synchronises an authorised recording/transcript.
11. AI generates a draft; teacher edits and approves it; only authorised students can access it.
12. Parent sees only linked child information.
13. User from tenant A cannot access tenant B data by changing an ID or request.
14. Revoked/expired tokens and unauthorised file URLs are rejected.

---

# PART XIII — IMPLEMENTATION PHASES & DELIVERABLES

## 41. Phased roadmap

### Phase 0 — Repository audit and discovery
- Inspect codebase and tooling.
- Identify existing features, defects, dependencies, and architecture.
- Confirm build/test commands.
- Produce a gap analysis and implementation plan.

### Phase 1 — Product and design foundation
- Product requirements.
- User journeys.
- Role/permission matrix.
- Teaching-mode workflows.
- Competitor research.
- `DESIGN.md`.
- Component inventory.
- Architecture diagrams.
- Database ERD and data dictionary.

### Phase 2 — Backend and database foundation
- Solution structure.
- SQL Server and EF Core.
- Migrations.
- Identity/authentication.
- Tenant context and isolation.
- Audit infrastructure.
- Shared API conventions.
- Idempotency and error handling.
- Demo seeder.

### Phase 3 — People and academic management
- Tenants/institutions.
- Users and roles.
- Parent relationships.
- Teachers and students.
- Courses, subjects, chapters.
- Batches, enrolments, and private teaching arrangements.

### Phase 4 — Scheduling
- Batch/group/one-to-one session creation.
- Recurrence.
- Time-zone engine.
- Calendar.
- Conflict detection.
- Notifications.

### Phase 5 — Learning Session Hub
- Session details.
- Participants.
- Google Meet/manual meeting links.
- Attendance.
- Notes and materials.
- Recordings.

### Phase 6 — Learning workflows
- Assignments/submissions.
- Doubt Centre.
- Follow-ups.
- Assessments/question banks.
- Progress.

### Phase 7 — AI Class Assistant
- Consent.
- Recording/transcript ingestion.
- Processing jobs.
- Draft generation.
- Teacher review and approval.
- Student learning assistant.

### Phase 8 — Web and PWA completion
- Role-specific responsive screens.
- Shared widgets.
- Offline-safe features.
- Push notifications.

### Phase 9 — Flutter applications
- Student, teacher, and parent experiences.
- Native capabilities.
- Shared API integration.

### Phase 10 — Hardening and release
- Security review.
- Accessibility review.
- Performance.
- Browser/device testing.
- Visual regression.
- Backups and restore.
- Deployment and operational documentation.

## 42. Required deliverables
1. Product requirements document.
2. Competitor comparison with sources.
3. Architecture diagrams.
4. User journey diagrams.
5. Role/permission matrix.
6. Database ERD and data dictionary.
7. SQL Server schema and migrations.
8. Seed data and safe reset instructions.
9. ASP.NET Core backend.
10. Responsive Blazor web application.
11. Installable PWA.
12. Flutter Android and iOS applications.
13. Google Meet/Calendar integration where supported.
14. Notification engine.
15. Global time-zone engine.
16. AI Class Assistant with approval workflow.
17. Automated unit/integration/E2E tests.
18. Playwright/Puppeteer test instructions and evidence.
19. Visual regression baselines where supported.
20. Deployment configuration and CI.
21. Environment example file without secrets.
22. API documentation.
23. Technical documentation.
24. Admin, teacher, student, and parent guides.
25. Production-readiness checklist.

---

# PART XIV — FIRST TASK AND OPERATING RULES

## 43. First task: inspect and plan before coding

Do not immediately rewrite or generate the whole project.

First:
1. Inspect the repository and report its current state.
2. Identify existing stack, versions, project structure, build status, database configuration, and test coverage.
3. Identify what already works and must be preserved.
4. Produce the product/module hierarchy.
5. Produce the role and permission matrix.
6. Define batch, group, and one-to-one user journeys.
7. Produce the architecture diagram.
8. Produce the ERD and data dictionary, explicitly showing that a learning session can exist without a batch.
9. Define ID, uniqueness, tenant-isolation, audit, concurrency, and deletion strategies.
10. Define API module boundaries.
11. Define the design system and component/widget library.
12. Define the seed-data plan.
13. Define test strategy and browser automation setup.
14. Present a phased plan with dependencies and risks.

If the project already exists, adapt the plan to it. Do not replace a functioning technology stack merely because another stack is preferred.

## 44. Definition of done

A feature is complete only when:
- The real backend/API workflow works.
- Data persists correctly.
- Database constraints protect integrity.
- Authorization and tenant isolation are tested.
- Loading, empty, error, and success states exist.
- The UI is responsive and accessible.
- Shared components are reused appropriately.
- Unit/integration/E2E tests pass.
- Browser screenshots have been inspected where applicable.
- No relevant console errors or failed requests remain unexplained.
- Documentation is updated.
- Known limitations are reported.

## 45. Final instructions

- Preserve approved Student Sync branding.
- Do not invent requirements or hide uncertainty.
- Do not hard-code production data.
- Do not duplicate business logic between web and Flutter.
- Do not force one-to-one sessions into batches.
- Do not allow cross-tenant access.
- Do not generate duplicate IDs or duplicate business records.
- Do not skip migrations, constraints, or tests.
- Do not publish unreviewed AI educational content.
- Do not expose recordings or children’s data.
- Do not leave dead buttons or fake charts.
- Do not remove working features without approval.
- Keep code clean, modular, reusable, and appropriately simple.
- Prefer shared widgets and services over repeated implementations.
- Use Playwright and Puppeteer as available to verify real browser behaviour.
- Fix defects rather than merely listing them.
- Keep the application runnable after each phase.
- Report completed work, tests run, results, screenshots/evidence, and remaining risks.

## FINAL PRODUCT OBJECTIVE

Student Sync should become a premium global learning ecosystem where institutions manage their operations, teachers deliver batch, group, and one-to-one tuition, students learn and resolve doubts, and parents monitor their children’s progress.

**One connected platform. One consistent brand. One shared backend. Web + PWA + native Flutter. Batch learning + group learning + one-to-one tuition.**

**STUDENT SYNC — LEARN • CONNECT • GROW.**
