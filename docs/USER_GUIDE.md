# Student Sync — user guide

Short guides for each role. Times are always shown in **your** time zone (set it under *Profile*); when a class was scheduled in a different zone, the organiser's time is shown too.

## Installing the app (everyone)

Student Sync can be installed from the browser and then opens from the home screen like any other app, with the Student Sync logo as its icon.

* **Android, Windows, Mac, Chromebook (Chrome or Edge):** a strip at the top of the page says *Install Student Sync*. Choose **Install** and confirm. If you closed the strip, use the browser menu → *Install app* (it returns by itself after 14 days).
* **iPhone and iPad:** Apple does not let websites show an install button. Tap the browser's **Share** button, then **Add to Home Screen**.

## Staying signed in (everyone)

*Keep me signed in* on the sign-in page is ticked by default: the app remembers you on this device for 30 days after you last used it, also after you close the browser or the installed app. **Untick it on a shared or public computer**, and use **Sign out** when you finish there. Changing your password signs you out on your other devices.

## Institution administrators

**Set up your institution**
1. *Settings → General & policies*: name, default time zone, currency, contact email. Decide whether recordings, transcription and the AI Class Assistant are allowed, how long recordings are kept, and who may message whom.
2. *Settings → Holidays*: add closure dates; recurring schedules can skip them.
3. *Academics*: add academic years and terms, grades, courses, and the subjects/chapters/topics inside each course. Assign teachers to subjects.
4. *Students → open a student → Diagnostic assessments → New diagnostic*: build a short entry test from the question bank (any subjects, basic questions, optionally with a diagram), name the teacher who runs it, publish it, then either let the student take it online or use *Enter paper answers* to type in what they marked on the paper sheet. The result page shows a breakdown by subject and chapter; tick the weak rows and *Record as weak areas* to add them to the learning plan with the source "Diagnostic test". Results are recommendations: staff decide what to record and can resolve it later.
5. *Students → open a student*: record the subjects or chapters they find hard (what the student, a parent or a teacher told you), set goals the student will see, and keep staff-only observations. Resolve a weak subject with a reason when it is no longer a problem; the history stays.

**Teachers first** — *People ▸ Teachers ▸ Add teacher*: name, email (for sign-in), phone/WhatsApp, the **subjects they teach** (one class, or *Whole subject (every class)* such as Mathematics for Class 1–12), qualifications (e.g. B.Ed, University of Calicut, 2016), years of experience, joining date, teaching languages and a short introduction. A teacher is offered in *Add classes* only for the subjects chosen here. Phone numbers are visible to admins only.

**Admit a student** — *People ▸ Students ▸ Admit student* (one page): student details and phone, parent/guardian name and phone (parents don't sign in), school, class and syllabus, **only the tuition subjects they need** (tap them; type a new one to add it to the centre's subject list), the last school result (overall %, marks per subject, the progress report as PDF/photo, notes), the joining date and what they want to improve. Everything is saved together; then share the one-time password and add their classes. The student's profile shows how long they have been with the centre, their tuition subjects, school results and goals. Teachers see the profile and results of students they teach, but not phone numbers.

**Add people** — *Teachers*, *Students* → "Add…". Students never register themselves: you create the profile (class, syllabus such as NCERT or Kerala State, academic year, school) and the account together. Each new account gets a one-time temporary password (shown once); the person must change it at first sign-in. If someone forgets their password, open their edit dialog and use *Reset password* for a new one-time password (their other sessions are signed out). Syllabi are managed under *Academics ▸ Syllabi*. Link parents to children under *Parents → Link child* and tick "verified" only after checking the relationship — parents see only verified children.

**Organise teaching**
* *Classes → Add classes* (or *Add classes* on a student's profile) books a student's one-to-one classes on one page: **who** (the student), **when** (how many weeks, tap the days, start time and class length — in UAE time, or tap the student's own time, e.g. "8 pm India time"), and **what is taught**: either the same subject every day, or *Different subject on different days* with a subject and teacher for each day (e.g. Monday Maths with Mirsha, Tuesday Science with Shahda). Only the student's own subjects and the teachers of each subject are listed; choose *Other subject…* or *Other teacher…* for special cases. Holidays are skipped and clashes are checked automatically (a teacher can't be double-booked; a student clash needs *Book anyway*). *Create N classes* saves the whole week at once and notifies the student and each teacher.
* Open any class to edit, reschedule (the original time is kept in *History*), cancel (optionally the rest of the series) or change participants.

**Fees (prepaid hours)** — open a student and use *Fees & hours ▸ Sell hours*: pick 10/20/30/50 hours (or type a number), the price, the day the hours count from, when it must be paid, and what was paid now (cash, card, bank transfer, UPI, cheque) with a receipt number. *Take payment* records the rest; a payment entered by mistake is *Removed* with a reason (it stays on record, crossed out); a package with no payments can be cancelled. Hours used are counted automatically from attendance — a class marked present or late uses its exact length (45 minutes = 0.75 h); absent or cancelled classes use nothing. When hours run out the panel says so; nothing is blocked — you decide whether to sell more hours or pause the student. *Fees* in the side menu and the *Fees due* card on the dashboard list who owes money (most overdue first) and who has 2 hours or less left.

**Monitor** — the dashboard shows real counts, upcoming classes, what needs you and fees due; attendance is under *Reports*. *Reports* filters by date and teaching mode and exports attendance as CSV. *Settings → Audit log* lists sensitive actions.

## Teachers

**Your subjects decide what you can create.** The admin adds your subjects to your profile (*People ▸ Teachers*). Everywhere you create something — classes, the question bank, exams and assignments — the subject list shows only those subjects, and the system refuses work in any other subject ("You can … only in the subjects you teach"). If a subject is missing, ask the admin to add it to your profile. *Students* shows the same table the admin uses, limited to the students you teach.

* **Dashboard** — next class, today's timetable, attendance still to mark, doubts to answer, submissions to grade, AI drafts to review.
* **Add classes** (*Classes → Add classes*): pick the student and subject, tap the days, set the time and class length, and choose how many weeks. Clashes are checked as you go; press *Create N classes*. You can only schedule yourself.
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
