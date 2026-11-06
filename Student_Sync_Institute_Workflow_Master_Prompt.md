# Student Sync — Institute Workflow & Product Master Prompt

## 1. Product vision

Build **Student Sync**, a production-ready tuition institute management and learning
platform for one institute. It must connect institute operations, student onboarding,
diagnostic assessment, prepaid learning-hour packages, teacher allocation, scheduling,
attendance, class content, assignments, examinations, textbook resources, and academic
progress in one coherent system.

This is not a conventional LMS where students must buy a separate course for every
subject. The institute sells learning hours. A student may use the purchased hours
across different subjects based on their needs and learning plan.

The product must be usable by:
- Institution administrators.
- Teachers.
- Students.

Parents do not have login accounts in the current approved scope. Admins may record
information supplied by parents. Do not introduce parent authentication unless the
owner explicitly approves a change.

This is a single-institute product. Preserve existing institution/tenant scoping and
security if present, but do not build new multi-institute SaaS onboarding or a
super-admin portal.

## 2. Core end-to-end student journey

The complete journey should work as follows:

1. A student arrives at the institute. There is no public student registration or self-sign-up.
2. The institution admin creates the student profile, including grade/class, syllabus, academic year, subjects, and required contact details.
3. The admin provisions the student's login account and securely issues the credentials directly to the student.
4. The institute identifies weak subjects through a diagnostic test, information provided by the student, information supplied by a parent to the admin, or a combination.
5. Admin/teacher reviews the result and creates or confirms the student's learning needs.
6. The student purchases a package of learning hours, for example 30 hours.
7. The student may request a teacher they already know, or the admin assigns a teacher. Different subjects may have different teachers.
8. Admin/authorized teacher schedules classes according to teacher availability, student availability, and the learning plan.
9. Teacher views the daily timetable, conducts the class, and records attendance.
10. After the class, teacher records the topics taught and publishes notes, reference videos, attachments, and assignments.
11. Student studies the materials and submits assignments.
12. Teacher creates exams or quizzes, including image-based questions where needed.
13. Student starts an exam; the server-enforced timer tracks the attempt and controls submission.
14. Teacher reviews subjective answers, marks submissions, and provides feedback.
15. Student and teacher can follow progress, attendance, learning history, and remaining package hours.
16. Student books/resources supplied at the beginning are available in the portal. Teachers can organise supported books into chapter-wise content with AI-assisted extraction, followed by teacher review.

The UI must make the current step and next action clear to each role.

## 3. Admin-only student account provisioning and credentials

This is a non-negotiable access-control rule for Student Sync:

- Students cannot register themselves. Do not build a public sign-up page, student registration endpoint, or `/register` route.
- Only an authenticated and authorized institution admin can create student profiles and provision student login accounts. Teachers must not create student accounts unless the owner explicitly changes this rule.
- The admin enters the student's details, creates the student profile, and provisions the linked login account. The admin then shares the login credentials directly with the student.
- Use a secure credential-delivery design: preferably issue a one-time invitation or temporary password, require a password change at first login, and never retain or display plaintext passwords.
- Students may change their password after signing in. Admins can initiate a password reset, activate/deactivate an account, or suspend access. Students cannot create accounts or elevate their roles.
- Use admin-controlled account states such as `Active`, `Inactive`, and `Suspended`. Do not implement a `PendingApproval` queue for student self-registration.
- Audit account creation, credential issuance, password reset, activation, deactivation, and suspension. Never write passwords, tokens, or secrets to logs.
- Treat the student profile and authentication account as related but distinct records. Handle partial failures safely so the system does not leave an unusable profile or orphaned login account.

## 4. Student profile and curriculum

Capture only information the institute needs and is authorized to process.

Student academic profile should include:
- Student identity and contact details.
- School/grade/class and academic year.
- Curriculum/syllabus (at minimum, support NCERT and Kerala State syllabus).
- Subjects studied.
- Subject/topic learning needs and self-reported weak areas.
- Diagnostic assessment history and results.
- Current learning goals and teacher/admin observations.
- Assigned teacher(s) by subject.
- Package and hour balance.
- Schedule, attendance, class reports, resources, assignments, exams, and results.

Do not assume a single syllabus or fixed subject list. Curriculum, grade, subjects,
chapters, and topics should be configurable and maintainable by authorized staff.

## 5. Initial diagnostic assessment

The institute needs a simple way to understand where a student needs support.

### Assessment authoring

Admin or an authorized teacher can:
- Create an assessment and give it a title, instructions, grade, syllabus, subjects,
  chapters/topics, duration (optional), and total marks.
- Add basic questions and configure correct answers/marks.
- Reuse questions from a question bank.
- Add images or diagrams to questions.
- Preview the assessment before assigning it.
- Assign it to an individual student or a suitable group.

Support a practical initial question set:
- Multiple choice.
- Multiple select where appropriate.
- True/false.
- Short answer.
- Numerical answer.
- Image/diagram-based question.

### OMR and paper-based option

The institute may conduct the diagnostic on paper using an OMR-style answer sheet.
Support a manual marking workflow in which the teacher/admin enters or verifies the
student's selected answers against an answer key.

Optical mark recognition can be added behind a replaceable integration if justified.
Do not advertise automatic OMR scanning until it has been implemented, tested, and
validated against real sample sheets. Provide a manual fallback.

### Student-selected weak subjects

A student can also select subjects or topics they find difficult. An admin can record
weak areas mentioned by a parent during admission. This route must work even if no
diagnostic assessment is taken.

### Results and learning plan

Show results by subject/topic, including attempted questions, correct/incorrect answers,
marks, and areas needing attention. Results should guide a learning plan and must not
permanently label or restrict a student. Authorized staff can review or override the
recommendation with a recorded reason.

## 6. Learning-hour packages, payments, and balance

This is a critical business rule.

The institute sells a defined quantity of tuition hours. For example, a student may
purchase 30 hours and use them for Mathematics, Physics, Chemistry, Biology, or other
subjects as agreed with the institute.

Requirements:
- Admin configures package name, number of hours, price, validity/expiry if applicable,
  and other package rules.
- Record purchase, invoice/receipt, payment status, payment method/reference, refunds,
  discounts, and manual adjustments as appropriate.
- Display purchased, consumed, and remaining hours clearly.
- Maintain an immutable or append-only hour transaction ledger with a reason and
  reference to the session/payment/adjustment.
- Support multiple packages or top-ups if the business rules allow them.
- Do not force a student to buy separate hours for each subject.
- Do not hardcode 30 hours as the only available package.
- Money and hour entitlements are separate records.

### Hour consumption rule

Default proposed rule: when a billable class is completed and attendance/session
completion is confirmed, deduct the actual billable session duration from the student's
hour balance. The institute must be able to configure its policy for student absence,
late arrival, cancellation, minimum billing increments, and teacher/admin corrections.

Never silently charge a cancelled or non-billable class. Every exception must be
configurable, visible, and auditable.

Use server-side transactions, concurrency control, and idempotency to prevent duplicate
deductions, negative balances, and duplicate payment processing. A payment provider
callback may be retried; it must not create duplicate entitlements.

Acceptance examples:
- A 60-minute completed billable session deducts 1.0 hour.
- A 90-minute completed billable session deducts 1.5 hours unless the institute has an
  explicitly configured billing increment.
- Replaying the same completion request does not deduct hours twice.
- A cancelled session does not deduct hours by default.
- A manual adjustment records who changed the balance, when, why, and by how much.

## 7. Teacher preference, matching, and assignment

During onboarding or after package purchase:
- Student can request a preferred teacher they already know.
- Admin can select/assign a teacher for the student.
- Admin can assign based on subject, syllabus, grade, availability, capacity, and
  student learning needs.
- Teacher assignment may differ by subject.
- Teacher preference is a request, not a confirmed assignment until approved.
- Preserve assignment history and effective dates.

Do not put a single required TeacherId on the student as the only assignment model.
Use an explicit student-teacher-subject relationship or an equivalent structure.

## 8. Timetable and class scheduling

Admin and authorized teachers can create sessions.

Session details should include:
- Subject and optional chapter/topic.
- Teacher.
- One or more participating students.
- Start date/time, end date/time, and planned duration.
- Delivery type: in-person, online, or institute-configured type.
- Location or meeting link where relevant.
- Teaching mode: batch, ad hoc group, private group, or one-to-one.
- Optional batch association.
- Session status and cancellation/reschedule reason.
- Billing/hour-consumption status.

A batch must not be mandatory for every session. Private and ad hoc classes must work
without fake batch records.

Teacher timetable:
- Daily and weekly views.
- Upcoming classes and class details.
- Student list and permitted student profile access.
- Conflict warnings for overlapping sessions and unavailable teachers.
- Reschedule/cancel workflow with notifications where supported.

Student timetable:
- Upcoming daily/weekly schedule.
- Subject, teacher, time, location/meeting details, and status.
- Clear changes when a class is rescheduled or cancelled.

Use consistent UTC storage and preserve the relevant time-zone identity. Handle
recurrences, daylight-saving changes, and local date boundaries correctly.

## 9. Attendance

Teachers need a simple attendance action for each scheduled session and participant.

Support configurable statuses such as:
- Present.
- Absent.
- Late.
- Excused.

Record marked-by, marked-at, notes/reason, and correction history. Prevent duplicate
attendance for a participant in the same session. Admins can review/correct records
with an audit trail.

Attendance and hour deduction must be related through an explicit institute billing
policy. Do not assume that every absent student is charged or that every present student
is automatically charged without checking session completion and billing state.

## 10. Post-class report, notes, and reference videos

After a class, teacher can create a class report:
- Subject, chapter, topic, and syllabus mapping.
- Topics covered and learning objectives.
- Notes/explanations.
- Student understanding and observations.
- Homework/follow-up instructions.
- Reference video links or uploaded videos.
- Attachments such as PDF, PPT/PPTX, DOC/DOCX, images, and approved formats.

Allow drafts, edits, and publication. Students see only published content shared with
them or their group. Keep the class report in the student's learning history.

Video access must follow the same authorization rules as the associated class/resource.
Do not expose private file URLs publicly.

## 11. Assignments

Teachers can create assignments for an individual student or a group:
- Title, instructions, subject, chapter/topic, and due date.
- Questions and marks, if applicable.
- Image/diagram attachments.
- PDF/PPT/document reference attachments.
- Optional rubric and expected answer.
- Submission type: text, uploaded file, image, or configured combination.

Students can:
- View instructions and due dates.
- Submit answers and files.
- See submission status and permitted feedback.
- Resubmit only when the assignment policy allows it.

Teachers can:
- Review each student's submission.
- Add comments and marks.
- Request corrections/resubmission.
- Track missing, late, and completed work.

Preserve submission versions and timestamps where resubmission is enabled.

## 12. Exams, question bank, and timed attempts

Teachers/admins can build reusable question banks and exams. Questions can be entered
manually or prepared with assistance from uploaded images/materials.

Support:
- MCQ and multiple select.
- True/false.
- Short/long written answers.
- Numerical answers.
- Image-based questions and diagrams.
- Marks, answer keys, explanations, and rubrics.
- Exam instructions, duration, availability window, attempts, and result visibility.

### Timed exam behaviour

- Student starts an attempt through an authenticated server request.
- Server records the official start time and calculates the deadline.
- The client displays a countdown but cannot control the official deadline.
- Save answers progressively where practical.
- Refresh/reconnect resumes the same valid attempt and remaining server time.
- At expiry, prevent further edits and submit/close according to exam configuration.
- Preserve the exact question/option snapshot shown to the student for that attempt.
- Prevent duplicate active attempts unless the exam permits them.
- Record start, autosave, submission, expiry, and marking events.

Auto-mark objective answers only when a valid answer key exists. Subjective answers
are marked by teachers. AI-assisted marking or feedback must be advisory and subject
to teacher review before publication.

## 13. Student books, PDFs, PPTs, and chapter-wise AI processing

Students may bring their own textbooks and study resources at the beginning of their
learning journey. Teachers/admins can upload those resources to the student's profile
or an authorized shared resource area.

Supported formats should be configurable. Start with PDF, PPT/PPTX, DOC/DOCX, and
common image formats if compatible with the existing stack.

### Processing pipeline

1. Validate extension, MIME type, file size, malware/security policy, and uploader
   permissions.
2. Store the original document and retain its identity/version.
3. Extract text and structural information.
4. Detect scanned PDFs and route them to OCR when configured.
5. Detect candidate chapter/unit/lesson headings and page ranges.
6. Build a proposed table of contents and chapter records.
7. Link each chapter to the original document and exact page range.
8. Let teachers review, edit, split, merge, rename, reorder, or reject detected chapters.
9. Publish approved chapters to the relevant student(s).
10. Track processing state, failures, retries, and source document version.

AI should assist with extraction and organisation, not invent missing textbook content.
Keep the original file intact and allow teachers to verify every chapter against the
source pages. Show processing warnings for low-quality scans or uncertain extraction.

Curriculum metadata should support NCERT and Kerala State syllabus and be extensible
for additional curricula. Do not assume the same chapter structure for every book.

Use background jobs for long-running OCR, extraction, and AI processing. Put external
AI/OCR providers behind replaceable interfaces. Apply consent, privacy, retention, and
data residency requirements before sending student files to external services.

## 14. Teacher portal and student 360 profile

Teachers should have a clear student list limited to their authorized assignments.
Opening a student should show a complete teaching-focused profile:

1. Student details and academic year.
2. Grade/class and syllabus.
3. Subjects and weak areas.
4. Diagnostic tests, attempts, and results.
5. Learning goals and progress by subject/topic.
6. Assigned teacher(s) and assignment history where relevant.
7. Package hours and relevant usage information according to teacher permissions.
8. Daily/weekly schedule and completed class history.
9. Attendance history and exceptions.
10. Class reports, notes, reference videos, and uploaded books.
11. Chapter-wise learning resources.
12. Assignments, submitted answers/files, due dates, marks, and feedback.
13. Exams, timed attempts, answer scripts, results, and marking status.
14. Teacher observations and recommended next steps.

Provide useful filters: subject, grade, syllabus, assessment status, attendance, pending
marking, and upcoming classes. Teachers must not see financial/admin-only information
unless explicitly authorized.

## 15. Role dashboards

### Admin dashboard
- Student accounts created by admins, credential issuance status, and active/inactive/suspended account counts.
- New student onboarding completion.
- Diagnostic assessments awaiting review.
- Students' weak-subject overview.
- Package sales, payment status, and hour balances.
- Teacher allocation and availability.
- Today's classes and timetable conflicts.
- Attendance exceptions.
- Upcoming assignments/exams.
- Submissions awaiting marking.
- Operational and academic reports.

### Teacher dashboard
- Today's and upcoming timetable.
- Assigned students.
- Attendance to record or complete.
- Class reports/notes to finish or publish.
- Assignments/exams to create.
- Submissions and exam answers awaiting marking.
- Student progress alerts and upcoming deadlines.

### Student dashboard
- Remaining learning hours and package details.
- Upcoming classes and assigned teachers.
- Learning plan and weak subjects.
- Recent class notes and reference videos.
- Books and chapter-wise resources.
- Assignments and due dates.
- Upcoming exams and remaining time when an exam is active.
- Results, feedback, attendance, and progress.

Use understandable metric definitions. Do not invent progress percentages or combine
unrelated metrics into a misleading score.

## 16. Notifications

Design notification events for relevant workflow changes:
- Admin-created student account provisioning, credential issuance, password reset, activation/deactivation, and suspension.
- Teacher request accepted or changed.
- New/rescheduled/cancelled class.
- Class notes/resources published.
- Assignment published and due-date reminders.
- Exam scheduled and exam result published.
- Submission feedback or resubmission request.
- Low hour balance or package expiry, if configured.

Use the notification channels already supported by the project. Do not claim email,
SMS, WhatsApp, or push notifications work until configured and tested. Avoid duplicate
notifications on retried events.

## 17. UX and design expectations

Create a professional, modern, clean EdTech product under the Student Sync brand.
Use the existing approved design system and brand assets; do not invent a competing
visual identity.

The experience should be:
- Role-specific and task-oriented.
- Responsive across desktop, tablet, and mobile.
- Accessible, with clear labels, focus states, contrast, and validation.
- Consistent in typography, spacing, cards, tables, forms, and navigation.
- Clear about loading, empty, error, success, disabled, and permission-denied states.

Prioritise efficient admin and teacher workflows. For example, attendance should be
quick to mark for an entire class; teachers should not need to open many separate
screens to publish a class report; student submissions should be easy to review.

Do not redesign unrelated screens or remove existing features without approval.

## 18. Technical architecture and implementation constraints

Follow the existing repository and the approved architecture. The current target
direction in the project instructions is:
- ASP.NET Core Web API on .NET 10 LTS.
- Entity Framework Core and SQL Server 2022.
- Clean Architecture principles in a modular monolith.
- Blazor Web App for the web experience.
- Flutter for native Android/iOS.
- SignalR for suitable live updates.
- Hangfire or repository-approved background processing.
- Object storage abstraction for documents and media.

Verify actual repository versions before implementation. Do not replace the stack or
perform a broad rewrite.

Use clear domain boundaries. Keep business rules out of UI components/controllers.
Use server-side authorization, validation, transactions, concurrency controls,
idempotency, audit logs, pagination, and secure file access.

Do not trust client-supplied student, teacher, session, role, or institution IDs.
Derive authorization from the authenticated identity and verify every relationship.

## 19. Security, privacy, and student protection

Student records, learning data, uploaded books, exam answers, and recordings are
private.

- Teachers see only assigned/authorized students.
- Students see only their own records and content shared with them.
- Admin access is role-controlled and audited.
- Private files use authenticated or short-lived controlled access.
- Validate uploads and enforce file size/type limits.
- Protect against IDOR/BOLA, privilege escalation, injection, XSS, CSRF where relevant,
  mass assignment, and insecure direct object references.
- Never expose answer keys before an exam's configured release time.
- Never expose one student's work, results, or profile to another student.
- Use fictional demo data.
- Obtain required consent before recording classes or processing recordings with AI.
- Do not send identifiable student data or student documents to external AI services
  without approved configuration, privacy review, and required consent.

## 20. Suggested project skills for Claude Code

Create or maintain focused skills under `.claude/skills/` only when useful and approved.
Do not assume they are already installed.

Recommended skill set:
1. `student-sync-domain` — domain map, terminology, business invariants.
2. `student-onboarding-assessment` — profile, curriculum, diagnostic tests, marking.
3. `hour-package-ledger` — packages, payments, balance, hour deduction, idempotency.
4. `teacher-allocation-scheduling` — teacher selection, availability, timetable.
5. `attendance-session-management` — participants, attendance, session completion.
6. `class-learning-content` — notes, class reports, videos, resource publication.
7. `assignments-exams` — question bank, submissions, timed exams, marking.
8. `document-chapter-processing` — secure uploads, OCR, AI extraction, chapter review.
9. `student-progress-360` — student profile, progress, history, dashboards.
10. `student-sync-security` — authorization, privacy, file security, audit.
11. `student-sync-testing` — unit, integration, UI, browser, and regression testing.

Each skill's SKILL.md should specify:
- When to use it.
- What repository files and documentation to inspect first.
- The domain rules that must not be broken.
- A step-by-step implementation workflow.
- Security and permission checks.
- Required tests and acceptance criteria.
- Completion/reporting checklist.

Use Plan Mode for broad or schema-changing work. Use Explore/read-only inspection
before modifying unfamiliar code. Use subagents only when available and suitable for
independent audits; the primary agent owns integration. Use hooks for repeatable local
quality checks, not as a substitute for application security. Verify all tools,
MCP servers, and skills before claiming availability.

## 21. Required development workflow

Before coding:
1. Read `CLAUDE.md`, this master prompt, README, and relevant design docs.
2. Inspect Git status and preserve all user changes.
3. Audit the existing architecture, schema, APIs, screens, and tests.
4. Run the current build/tests where practical and record the baseline.
5. Map the requested workflow to existing modules and identify gaps.
6. Provide a short implementation plan and acceptance criteria.

During implementation:
1. Work in small, complete vertical slices.
2. Update domain, database, API, authorization, UI, and tests together.
3. Review migrations before applying them.
4. Preserve existing behaviour unless a change is required and approved.
5. Keep AI-generated content as drafts until teacher review where appropriate.
6. Add useful loading, empty, error, and permission states.
7. Do not leave primary actions disconnected from real persistence/API behaviour.

After implementation:
1. Run relevant tests, formatting, linting, and builds.
2. Verify important user journeys in a browser/device where available.
3. Test role permissions and student data isolation.
4. Inspect the final diff for regressions, security issues, and accidental scope.
5. Update documentation.
6. Report completed work, exact verification performed, limitations, and next steps.

Never claim that a feature, test, integration, migration, or deployment works unless
it has actually been verified.

## 22. Suggested implementation phases

Phase 0 — Repository and product audit.
Phase 1 — Student profile, curriculum, subjects, and learning needs.
Phase 2 — Diagnostic assessment creation, attempt, marking, and review.
Phase 3 — Learning-hour packages, payments, hour ledger, and balance.
Phase 4 — Teacher preference, matching, and subject-specific assignment.
Phase 5 — Teacher availability, scheduling, sessions, and attendance.
Phase 6 — Class reports, notes, videos, and resources.
Phase 7 — Assignments, submissions, marking, and feedback.
Phase 8 — Question bank, timed exams, attempts, and results.
Phase 9 — Book upload, extraction/OCR, chapter organisation, and teacher review.
Phase 10 — Teacher student-360 view, progress, dashboards, and reports.
Phase 11 — Notifications, hardening, accessibility, performance, and end-to-end tests.

Do not start all phases at once. Recommend the smallest valuable complete slice based
on the actual repository state.

## 23. Important open business decisions

Do not silently invent these rules. Inspect existing implementation and ask the owner
when a decision is required:

- Are packages valid indefinitely or do they expire?
- What happens when a student is absent or late?
- Is billing by exact minutes, 30-minute blocks, 1-hour blocks, or another increment?
- Can unused hours be refunded or transferred?
- Can a student attend a group class and, if so, how are hours charged per student?
- Can students request teacher changes after assignment?
- Are exams always scheduled or can teachers publish practice quizzes?
- Can students retry assignments/exams, and how many times?
- Which file sizes and formats are allowed?
- Which AI/OCR provider is approved, and where may student documents be processed?
- Which notification channels are actually configured?

For a decision that blocks a safe implementation, ask one focused question. Otherwise
document the assumption and keep it configurable.

## 24. Definition of done

A workflow is complete only when:
- It matches the approved business rules.
- The correct roles can perform it and unauthorized roles cannot.
- Data persists through real APIs/database operations.
- Validation, error, loading, empty, and success states work.
- Database relationships and migrations are safe.
- Relevant tests are added and run.
- Important browser/device flows are verified where possible.
- Student data and private files are protected.
- Documentation is updated.
- The completion report distinguishes verified behaviour from unverified work.
