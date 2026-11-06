# Student Sync — user guide

Short guides for each role. Times are always shown in **your** time zone (set it under *Profile*); when a class was scheduled in a different zone, the organiser's time is shown too.

## Institution administrators

**Set up your institution**
1. *Settings → General & policies*: name, default time zone, currency, contact email. Decide whether recordings, transcription and the AI Class Assistant are allowed, how long recordings are kept, and who may message whom.
2. *Settings → Holidays*: add closure dates; recurring schedules can skip them.
3. *Academics*: add academic years and terms, grades, courses, and the subjects/chapters/topics inside each course. Assign teachers to subjects.
4. *Students → open a student → Diagnostic assessments → New diagnostic*: build a short entry test from the question bank (any subjects, basic questions, optionally with a diagram), name the teacher who runs it, publish it, then either let the student take it online or use *Enter paper answers* to type in what they marked on the paper sheet. The result page shows a breakdown by subject and chapter; tick the weak rows and *Record as weak areas* to add them to the learning plan with the source "Diagnostic test". Results are recommendations: staff decide what to record and can resolve it later.
5. *Students → open a student*: record the subjects or chapters they find hard (what the student, a parent or a teacher told you), set goals the student will see, and keep staff-only observations. Resolve a weak subject with a reason when it is no longer a problem; the history stays.

**Add people** — *Teachers*, *Students* → "Add…". Students never register themselves: you create the profile (class, syllabus such as NCERT or Kerala State, academic year, school) and the account together. Each new account gets a one-time temporary password (shown once); the person must change it at first sign-in. If someone forgets their password, open their edit dialog and use *Reset password* for a new one-time password (their other sessions are signed out). Syllabi are managed under *Academics ▸ Syllabi*. Link parents to children under *Parents → Link child* and tick "verified" only after checking the relationship — parents see only verified children.

**Organise teaching**
* *Batches* are optional cohorts for regular classes. Add students to a batch and schedule "Batch class" sessions for it.
* *Private tuition* holds one-to-one and private-group arrangements — no batch needed.
* *Sessions → New session*: choose **Batch class**, **Group session**, **One-to-one** or **Private group**, pick participants, set date/time/time zone, and optionally make it repeat (weekly days or daily, until a date or a number of sessions). "Preview & check conflicts" shows every occurrence in your time, any daylight-saving adjustments, and clashes. A teacher can't be double-booked; student clashes need confirmation.
* Open any session to edit, reschedule (the original time is kept in *History*), cancel (optionally the rest of the series) or change participants.

**Monitor** — the dashboard shows real counts, upcoming sessions, attendance, sessions by teaching mode and alerts. *Reports* filters by date and teaching mode and exports attendance as CSV. *Settings → Audit log* lists sensitive actions.

## Teachers

* **Dashboard** — next class, today's timetable, attendance still to mark, doubts to answer, submissions to grade, AI drafts to review.
* **Schedule** a one-to-one or group session from *Sessions → New session* or from a student's profile ("Schedule 1:1"). You can only schedule yourself.
* **Session hub** (open a session):
  * *Overview* — details, participants, join link, mark live/completed.
  * *Attendance* — opens 30 minutes before the start; works the same for one student or many. Parents are notified of absences.
  * *Notes* — write in Markdown (tables, `$formulas$`); save as draft, publish when ready. Tick "students may save offline" for revision notes.
  * *Materials* — links or files (PDF, slides, worksheets).
  * *Recordings* — upload only after every participant (or guardian) has consented on the *Consent* tab and your institution allows recordings.
  * *Assignments & doubts* — work linked to this class.
  * *AI recap* — upload the transcript from your meeting tool (VTT/SRT/TXT), then "Generate AI draft" (only when your institution has configured an AI provider and everyone consented). Review on the draft page: edit, approve, then publish. Students never see AI content you haven't approved.
* **Prepare class** — the agenda includes unresolved doubts automatically carried forward from your students; mark them discussed and the student is asked to confirm.
* **Assignments** — create (link to a session, a batch, or pick students), attach files, publish, grade with feedback or return for corrections.
* **Exams** — build questions in the *Question bank* (single/multiple choice, true/false, short/long answer, fill-in, matching, numerical; set the chapter and attach a diagram where useful), create an exam with an open window and duration, publish, grade written answers, then release results. A *Diagnostic* exam can mix subjects and be answered on paper: use *Enter paper answers* on the exam page, then record weak areas from the breakdown.
* **Availability** — your usual weekly teaching hours.

## Students

* **Dashboard** — next class with a Join button, upcoming classes, assignments due, open doubts, recent notes/recordings, and your progress (each measure explains how it's calculated).
* **My classes** — list or month view; open a class for notes, materials and recordings (resume where you stopped).
* **Assignments** — write an answer and/or attach files, save drafts, submit. Late work is accepted only if your teacher allows it.
* **Exams** — start during the open window; answers save automatically; the timer is enforced by the server. Results appear when your teacher releases them.
* **Doubt Centre** — ask a question (attach a photo, PDF or voice note, link it to a class or a recording timestamp). If it isn't resolved it's added to your next relevant class. When your teacher answers, confirm it's resolved or ask a follow-up.
* **AI assistant** — answers only from notes your teachers approved; it tells you when it can't help and lets you send the question to your teacher.
* **Consent** — on a class's *Consent* tab you can grant or withdraw consent for recording, transcription and AI processing.
* **My learning plan** (side menu or *Profile*) — your syllabus and class, the goals your teachers set with you, and the subjects you find hard. Add a subject (and chapter) yourself when something feels difficult; your teachers see it and plan around it. You can remove what you added, but not what a parent or the institute recorded.

## Parents and guardians

* **Family dashboard** — switch between your children; see upcoming classes, recent attendance, assignments and marks, released exam results and progress.
* **Calendar** — your children's classes in your time zone; export to your calendar app (.ics).
* **Messages** — contact your children's teachers if your institution allows it.
* **Notifications** — absences, new assignments, results, schedule changes and a weekly summary. Choose email and quiet hours under *Profile*.

## Platform administrators

* *Institutions* — create an institution with its first administrator (30-day trial), change plan/subscription status (history kept), suspend/reactivate (signs users out).
* *Plans* — limits (students, teachers, storage, AI jobs) and features.
* *Dashboard* — aggregate health and which integrations are configured. Platform administrators can't view institutions' classes, notes or student work.
