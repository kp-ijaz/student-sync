# Student Sync — Design System

**LEARN • CONNECT • GROW.** Student Sync is a calm, trustworthy workspace for tuition centres, teachers, students and parents. The interface should feel organised and friendly, never noisy. Every screen answers "what do I need to do next?" first.

This document is the single source of truth for web (Blazor, `src/StudentSync.Web/wwwroot/css/app.css` `:root` tokens). The Flutter token file (`mobile/student_sync/lib/core/theme/tokens.dart`) mirrors it, but the Flutter app is **parked for this phase** (owner decision 2026-11-04): change web tokens only, and re-sync Flutter when the app is re-enabled.

**One look on every platform (2026-11).** The owner approved the Flutter app's visual language; the web app now uses the same radii, shadows, filled inputs, gradient, photos, illustrations and loading mark. The one deliberate difference is navigation: the web keeps its grouped side menu (§4) and never shows a bottom bar or top navigation.

## 1. Brand tokens

| Token | Hex | Use |
|---|---|---|
| `--ss-navy` Deep Navy | `#0B2854` | Sidebar, headings, primary text on light surfaces |
| `--ss-blue` Royal Blue | `#1264D8` | Primary actions, links, focus rings, selected states |
| `--ss-orange` Vibrant Orange | `#FF9F24` | Restrained accent: "live"/"next" highlights, brand mark. Never for large fills or body text |
| `--ss-white` | `#FFFFFF` | Cards, inputs |
| `--ss-bg` Soft Background | `#F3F6FC` | App background |
| `--ss-slate` Slate Grey | `#64748B` | Secondary text, icons |
| `--ss-success` | `#16A36A` | Present, resolved, graded, success toasts |
| `--ss-warning` | `#F59E0B` | Late, due soon, pending review |
| `--ss-error` | `#E5484D` | Absent, failed, destructive actions, validation |
| `--ss-info` | `#0EA5E9` | Informational badges |
| `--ss-border` | `#E2E8F0` | Card and input borders, dividers |
| `--ss-blue-50` Light Blue | `#EAF2FF` | Selected rows, info panels, primary-tinted backgrounds |
| `--ss-orange-50` Light Orange | `#FFF4E5` | Accent panels (e.g. next class) |
| `--ss-blue-deep` | `#0A4BB0` | Middle stop of the brand gradient |
| `--ss-fill` | `#EFF3FA` | Input fill, list dividers, neutral TagPill, progress-bar track |
| `--ss-hairline` | `rgba(11,40,84,.08)` | Card and pill-track borders |
| `--ss-orange-deep` / `--ss-orange-text` | `#C2410C` / `#9A5B00` | AA-contrast orange for icons / text on light surfaces |
| `--grad-brand` | navy → `#0A4BB0` (55%) → blue, 135° | See §3 for where the gradient may appear |

Text colours: primary `#0B2854`, body `#1E293B`, secondary text `#475569` (≥7:1 on white and on Soft Background). Slate Grey `#64748B` is used for icons, borders and large text only — it measures 4.3:1 on Soft Background, below AA for body text (found by the axe audit). Orange text is not used for body copy (insufficient contrast); orange appears as fills behind navy text or as a 3 px accent bar.

**Logo** (approved 2026-11): an open book with blue and purple pages and an orange star, with the "Student Sync" wordmark (navy *Student*, purple *Sync*). Files, and where the mark appears alone or with the wordmark, are in `docs/brand/README.md`. On navy or the brand gradient the mark always sits on its white tile with the light wordmark.

**Dark mode** is not shipped in v1 (documented limitation); tokens are defined as CSS custom properties so a dark palette can be added without touching components.

## 2. Typography

- Headings: **Plus Jakarta Sans** (600/700). Body, tables, numbers: **Inter** (400/500/600), `font-variant-numeric: tabular-nums` for statistics.
- Scale (px / line-height): Display 28/36 · H1 24/32 · H2 20/28 · H3 16/24 · Body 14/22 · Small 13/20 · Caption 12/16.
- Headings are never larger than 28 px inside the app shell. Sentence case everywhere ("Schedule one-to-one", not "Schedule One-To-One").
- Arabic uses the system Arabic stack (`"Noto Sans Arabic", Tahoma`) and the page switches to `dir="rtl"`.

## 3. Spacing, radius, elevation

- Spacing scale: **4, 8, 12, 16, 24, 32, 48, 64** px (`--sp-1` … `--sp-8`). No other values.
- Radius (web and mobile): cards **20 px** (`--r-card`), controls — buttons, inputs, selects — **14 px** (`--r-control`/`--r-btn`), sheets and dialogs **28 px** (`--r-sheet`), pills, segmented tracks, badges and avatars **999 px**. Hero cards use 24 px.
- Elevation: cards are white with a 1 px hairline (`--ss-hairline`) and a soft diffuse navy shadow `--shadow-1: 0 1px 2px rgba(11,40,84,.04), 0 8px 24px rgba(11,40,84,.06)` (= `SsShadow.card`). `--shadow-2` for menus, dialogs and hovered link cards. No coloured or glowing shadows.
- Controls: inputs/selects/textareas are **filled** (`--ss-fill`) with no resting border and a **1.6 px blue border** (+ faint ring) on focus; buttons are **48 px** tall (`.btn-sm` and `.select-sm` 36 px in dense desktop tables; on phones < 768 px they grow to 44 px, as do toggle-group buttons, `.check` rows and chip remove buttons, so every touch target is ≥ 44 px). `.btn-pill` makes a fully rounded button (sign-in, state actions).
- Gradient (`--grad-brand`) only on: the sign-in hero, `HeroCard` (profile header, progress summary, a role's home hero), the selected pill of `Segmented`/`PillTabs`/`.segmented`/`.tabs`, and the initials avatar. Nowhere else.

## 4. Layout & breakpoints

| Name | Width | Shell |
|---|---|---|
| Mobile | < 768 px | Mobile-app shell: app bar (menu button, page title, bell, photo avatar) + floating four-tab bar + side menu as a drawer; single column; tables become stacked cards; calendar becomes a compact month grid with dots |
| Tablet portrait | 768–1023 px | Same mobile-app shell (app bar + floating tab bar + drawer); 2-column grids |
| Tablet landscape / desktop | ≥ 1024 px | Fixed 248 px navy side menu (always visible); content max-width 1280 px |

Navigation is a single side menu with collapsible groups (for example *Classes ▸ Timetable / Add classes / Pre-recorded videos*), defined per role in `Navigation.For` (`Services/UiServices.cs`). The group containing the current page opens automatically, and exactly one link is marked current (the longest matching path). There is no top navigation.

**Phones and tablets (< 1024 px) use the Flutter app's shell (owner request 2026-11-04: the PWA must match the mobile app).** `Navigation.Tabs(role)` defines the same four tabs as `role_shell.dart` — student *Home · Subjects · Learning · Doubts*, teacher *Home · Classes · Students · Tasks*, admin *Home · Students · Classes · Reports* — rendered by `MainLayout` as a floating bar (16 px side margins, 12 px above the home indicator via `env(safe-area-inset-bottom)`, 72 px high, 28 px radius, frosted white, floating shadow). The selected tab is a navy pill with icon + label and grows to twice the width; the others are icon-only fill tiles with tooltips. A tab also owns its child routes (for highlighting only, e.g. `/timetable` and class pages under *Subjects*; `/assignments`, `/exams`, `/videos`, `/learning-plan` under *Learning*). Everything not in the four tabs stays reachable from the drawer (menu button). Content keeps 100 px + safe area of bottom padding so the last card and the toasts clear the bar; a page's single primary action may float above the bar with `.fab-mobile` (used by Doubt Centre's *Ask a doubt*, like the mobile FAB). On desktop (≥ 1024 px) nothing changes: docked side menu, no tab bar.

- **Student:** Dashboard · Subjects (`/subjects`) · Timetable (`/timetable`) · Learning (`/learning`, one page with pill tabs Assignments · Exams · Videos; `/assignments`, `/exams`, `/videos` keep working) · Doubt Centre · AI assistant · *Communication ▸* Messages, Announcements. No Library (notes belong to each class page; `/library` still works for existing links) and no Progress item (it opens from Profile → *My progress*).
- **Teacher:** Dashboard · *Classes ▸* Timetable (`/sessions` calendar), Add classes, Pre-recorded videos, Availability · *Teaching ▸* Students, Tasks (`/teacher/tasks`), Assignments, Exams, Question bank, Doubt Centre · *Communication ▸*.
- **Admin:** unchanged.

The top bar sits on the page background (translucent, no white strip). On desktop it holds, at the end, white circle buttons for Messages, Notifications (with a red count) and Sign out, then the user's photo avatar (initials fallback) linking to Profile. Below 1024 px it becomes the mobile app bar: menu button, the **page title** (published by `PageHeader` through `ShellState`, 22/28 Jakarta navy; the page's own `h1` is then visually hidden but kept for assistive tech and focus; dashboards without a `PageHeader` show the tab name), then only the bell and the avatar — Messages moves to the drawer (with its unread count) and Sign out to Profile, as in the app. The dashboards use the app's `HomeGreeting` header (photo, "Good evening", first name).

Page anatomy: `PageHeader` (title, optional subtitle, primary action on the right) → optional `FilterBar` → content cards. One primary action per page.

**Phones (< 768 px), 2026-11 refresh:** `PageHeader` actions fill the width; `.form-actions` stack full-width in markup order (primary last, nearest the thumb) — visual order always equals focus order; `Modal` becomes a bottom sheet (28 px top corners, grab handle, actions sticky at the bottom, above the home indicator); toasts span the width above the tab bar. `table.stack-mobile` rows become cards: the first cell is the card title (no label), the other cells are *label | value* rows that wrap long text, and the unlabelled action cell is a row of full-size buttons at the bottom. Tables that are not stacked always sit in a `.table-wrap` so only the table scrolls, never the page. On tablets (768–1199 px) the admin dashboard's side cards sit two-up (`.dash-aside`).

## 5. Components (web: `Components/Shared`, Flutter: `lib/core/widgets`)

A live gallery of the web components with sample data is at **`/dev/ui`** (development environment only).

| Web component / class | Flutter | Purpose and key parameters |
|---|---|---|
| AppShell / Sidebar / TopBar (`MainLayout`) | `RoleShell` | Role-aware side menu from `Navigation.For`; active item = lighter background + 3 px orange bar |
| `PageHeader` | app bar title | Title, subtitle, optional breadcrumb and one primary action |
| `.card` (+ `.tight`, `a.card`) | `SsCard` | White, 20 px radius, hairline + soft shadow, 24 px padding (16 px on phones) |
| `HeroCard` (`.hero-card`) | `HeroCard` | Gradient card; content white. `AriaLabel` |
| `IconTile` | `IconTile` | Icon on a tinted rounded square. `Icon`, `Tone` (blue, navy, green, orange, amber, red, slate, violet, info), `Size` (sm 32 / md 38 / 40 / lg 48) |
| `Badge` (`.badge.dot`) | `StatusBadge` | Status pill: tint + dot + text from `StatusStyles.For(...)`. `Style`, `Dot` |
| `TagPill` | `TagPill` | Neutral filled label (teaching mode, codes). `Label`, `Icon` |
| `GlassPill` | `GlassPill` | Translucent pill for text on photos/heroes. `Label`, `Icon`, `Live` (orange dot) |
| `SectionHeader` | `SectionHeader` | Icon tile + h2 title + count pill + optional "See all ›". `Title`, `Icon`, `Tone`, `Count`, `LinkHref`/`LinkLabel`, `Trailing`, `Level`, `Id` |
| `Segmented<TValue>` (`.seg`) | `SsSegmented` | Single choice: rounded track, gradient pill slides under the choice; native radios (arrow keys). `Options` (`Option(Value, Label, Icon?)`), `@bind-Value`, `Label` |
| `PillTabs<TKey>` | `SsTabBar` | ARIA tablist + tabpanel, sliding gradient pill, ←/→/Home/End (`js/app.js`). `Tabs` (`Tab(Key, Label, Count?, Icon?)`), `@bind-Active`, `Label`, panel as child content. Only for parallel lists, ≤ 4 tabs |
| `.segmented`, `.tabs` | — | Existing aria-pressed toggle groups and role=tablist bars, restyled to the same rounded track + gradient selection |
| `ListGroup` + `ListRow` | `SsListGroup`/`SsListRow` | Inset grouped settings rows. Row: `Icon`, `Tone`, `Title`, `Subtitle`, `Href` (link) or `OnClick` (button), `Trailing`, `ShowChevron` |
| `ShortcutGrid` | `ShortcutGrid` | Exactly four compact pill links in a 2×2 grid. `Items` = 4 × `ShortcutItem(Label, Href, Icon, Tone)` |
| `InlineEmpty` | `InlineEmpty` | One quiet "nothing here" line inside a section. `Text`, `Icon` |
| `ClassCover` | `ClassCoverImage` | Subject cover photo + navy fade + overlay slot. `Subject`, `Title`, `Height`, `Href`, `ShowTitle`, `Shade`. Photo from `ClassCovers.For(subject, title)` (same keyword order as `class_cover.dart`) |
| `Avatar` | `UserAvatar` | Photo or initials (first + last, bracketed suffixes ignored) on the gradient. `Name`, `PhotoUrl`, `Size` (sm/""/md/lg/xl), `Ring`, `Decorative` |
| `TeacherPhotoCard` | `TeacherPhotoCard` | Portrait card: photo (or initials), name + subject over a fade, arrow tile when `Href` is set |
| `StatCard`, `MetricTile` | `StatCard`, `MetricTile` | Figures from the API only. `StatCard`: `Label`, `Value`, `Hint`, optional `Icon` + `Tone` (an `IconTile` beside the label; dashboard stats map their `StatDto.Key` through `StatIcons.For`, never the label). Lay out stat rows with `.stat-grid` (rows always fill: 2-up on phones, no holes). `MetricTile` expands (details/summary) to "How is this calculated?" |
| `BrandSpinner`, `LoadingView` / `Skeleton` | `BrandSpinner`, `LoadingView` | `LoadingView` renders a `Skeleton` (shimmering placeholders shaped like the content: `list`, `page`, `block`), used by `DataView`, route authorisation and redirects. `BrandSpinner` (the logo's turning star) remains for the native app |
| `StateView` | `EmptyState`/`ErrorState`/`ForbiddenState` | Illustration (`Illustrations.*`: empty, search, offline, server_error, access_denied, not_found, all_done, questions, mailbox) + title + message + actions; `Compact` shows the icon on two soft discs |
| `DataView` | `AsyncView` | Every data region: loading → error (offline/server/403/404 illustration, Retry) → empty (`EmptyIllustration`, or derived from `EmptyIcon`) → content. `Compact` for tight cards |
| `SessionItem` / SessionCard | `SessionCard` | Time in viewer's zone (+ organiser zone when different), type badge, Join |
| `Modal`, `Toasts` | dialogs, snack bar | Focus-trapped dialogs (28 px radius; bottom sheet on phones); toasts announced politely |
| `InstallBanner` | — (native app) | Offer to install the PWA: a white strip at the very top of the sign-in page and of the shell (above the app bar, scrolls away) with the mark, "Install Student Sync", an **Install** button (36 px; 44 px below 1024 px) and a 44 px *Not now* icon button. Rendered hidden; `js/app.js` shows it when the browser reports the app is installable (`beforeinstallprompt`), or with the Share-menu steps on iOS, which has no install API. Never shown inside the installed app; hidden for 14 days after *Not now*. Present in both layouts, so it also appears on `/dev/ui` |
| `PersonRow` (`ul.plist`) | — | Compact person list for phones and tablets (< 1024 px; desktop keeps the table, wrapped in `.desk-list`): avatar, name (two lines max), one facts line (`Meta`), optional `Detail` line, optional `Status` badge (only for a non-normal state, e.g. Inactive), up to two 44 px `icon-btn` `Actions` each named for the person. The row opens the person (`Href`) or a dialog (`OnOpen`). Used by Students and Teachers |
| `HoursPanel`, `PersonCalendar` | — | Attendance & hours for one person (This month / All time) and a month calendar of their classes; used on student and teacher profiles |
| `.badge.removable` + `.chip-remove` | — | A chip whose text wraps, with an icon remove button (`aria-label` names what is removed); 44 px target on phones |
| `PhotoEditor` | profile photo sheet | Upload/change/remove a photo; optional `Info` content beside the photo |
| `TimeZoneSelect` | time-zone picker | IANA ids with the current offset |
| `Student/AssignmentCard` | `AssignmentCard` | One assignment for a student: clipboard tile (green; red when overdue and not handed in), title + submission badge, "Due … · marks" (red when overdue), teacher · class. Used by Learning, `/assignments`, the student home and the class page |
| `Student/ExamCard` | `ExamCard` (learning_screen.dart) | One exam for a student: navy tile, title + status ("In progress" while an attempt is open), subject · questions · marks · minutes, window and attempts, best score or release note, then *Start exam* / *Resume exam* / *View result* |
| `MainLayout` tab bar (`.tabbar`) | `FloatingNavBar` (role_shell.dart) | Phone/tablet floating four-tab bar; see §4 |
| AIReviewPanel / ApprovalBar | — | AI content always labelled "AI draft — not visible to students until approved" |

Pages: `NotFound` (404 illustration + "Go to home"), `Forbidden` and the router's not-authorised state (access_denied illustration). Photos live in `wwwroot/images/covers`, `wwwroot/images/welcome` (Pexels, see `images/CREDITS.md`); illustrations in `wwwroot/illustrations` (unDraw, see its `CREDITS.md`).

Status colour mapping (single source: `StatusStyles.cs` / `status_styles.dart`):
Scheduled → blue · Live → orange · Completed → green · Cancelled → slate (strikethrough title) · Present → green · Late → amber · Absent → red · Excused → slate · Doubt New/Assigned → blue · InProgress → info · Answered/PendingConfirmation → amber · Resolved → green · Reopened → red · AI AwaitingTeacherReview → amber · Approved → blue · Published → green · Rejected/Failed → red.

Teaching mode labels: **Batch class**, **Group session**, **One-to-one**, **Private group**.

## 6. Accessibility

- WCAG 2.2 AA: contrast ≥ 4.5:1 for text, 3:1 for UI boundaries and focus indicators.
- Visible focus: 2 px blue outline + 2 px offset on every interactive element; never remove outlines.
- All inputs have `<label>`s; errors are linked with `aria-describedby`; status changes announced via a polite live region (toasts).
- Touch targets ≥ 44×44 px on mobile.
- Times always include the time zone abbreviation/IANA id when it differs from the viewer's.
- Don't rely on colour alone: badges carry text; attendance uses text + colour.

## 7. Motion

150–200 ms ease-out for hover/press and drawer transitions; no parallax or decorative animation. Respect `prefers-reduced-motion` (disable transitions).

## 8. Voice

Plain, warm, specific. Say what happened and what to do next ("Attendance saved for 5 students", "This exam opens on Mon 3 Nov at 19:00 (Asia/Dubai)"). Never claim an external action succeeded unless the API confirmed it (e.g. "Email delivery is not configured" rather than "Email sent").

## 9. Native mobile app (Flutter) — 2026-11 refresh — PARKED

> **Out of scope this phase (owner decision 2026-11-04).** Kept as the record of the approved mobile design for when the app is re-enabled. Do not apply or verify these rules now; this phase ships the web portals and PWA only.

The Flutter app shares the brand colours, typefaces and voice above. Since 2026-11 the web uses the same surface language (§3, §5); what remains mobile-only is the bottom navigation, the splash/welcome flow and touch-specific patterns. The web keeps its side menu (§4).

- **Navigation:** a floating white bottom bar with **at most four tabs** (asserted in `RoleShell`); the selected tab is a navy pill showing icon + label, the others are icon-only rounded squares (tooltips and screen-reader labels on all). Student: Home · Subjects · Learning · Doubts. Teacher: Home · Classes · Students · Tasks. Profile is not a tab: it opens from the initials avatar at the top right of every tab, next to the notification bell. Tapping the current tab returns it to its first screen.
- **Icons:** Lucide (`lucide_icons_flutter`, MIT) everywhere, 2 px stroke. Icons that lead a row sit on a tinted rounded square (`IconTile`) in the row's semantic colour.
- **Radius:** cards 20 px, controls 14 px, sheets/dialogs/bottom bar 28 px, pills 999 px.
- **Elevation:** cards are white with a hairline (`#0B2854` at 8%) and a soft diffuse navy shadow; no coloured or glowing shadows.
- **Photography:** real photos only, never illustrated people. People: the profile photo uploaded by the user or an admin (teachers/admins seeded with Pexels stock photos in development; students show initials unless they upload one). Classes: one bundled cover photo per subject area (`ClassCovers`, assets/images/covers), with a navy fade at the bottom for overlaid text. Welcome screens: three bundled photos. Credits live next to the files.
- **Cards with photos:** next-class card (cover photo + teacher photo + Join), "Coming up" carousel cards, "Your teachers" portrait grid, class-details cover header with white fact pills. Text on photos uses glass pills (navy 20 % + white border) — never light badges.
- **Illustrations:** unDraw, recoloured to Royal Blue, for empty, search-with-no-results, offline, server error, access denied and 404 states only.
- **Loading:** the logo's book with its star turning (`BrandSpinner`), also on the splash screen.
- **First run:** splash (soft white `#F6F7FE` like the native launch screen, turning star, wordmark below) → three welcome slides (shown once per device; only Skip and Next/Get started) → sign-in (accounts are issued by the institute office; there is no sign-up).
- **Gradient:** the navy → blue gradient (`SsGradients.brand`) is used for the sign-in hero, the profile header, the selected segmented-control pill and the initials avatar. Nowhere else.
- **App bars** sit on the page background (no white bar) with a 22 px Plus Jakarta Sans title. Home tabs show the mark, a time-of-day greeting and the first name.
- **Subjects (students):** the institute follows CBSE/NCERT (Classes 1–12; Science/Commerce/Humanities in 11–12). *Subjects* groups the student's subjects as **Core** (same for everyone in the standard), **Languages** (their second/third language, labelled with the slot) and **Additional** (subjects the admin added for that student only), as a 2-column photo grid with teacher, progress and next class. A subject opens on a cover header, counts and the next class, then three pill tabs: **Videos** (watchable first, then "Unlocks later"), **Classes** (an Upcoming | Past switch; a class opens its notes, assignments and recording) and **Syllabus** (chapters with status markers; the chapter being taught opens by itself). Notes are never pooled per subject: they belong to the class they came from.
- **Timetable:** a week strip (dots on days with classes) and the chosen day hour by hour — start/end time on the left, a subject-coloured card (cover thumbnail, teacher, status or Join when live) on the right; a Day/Month switch keeps the month calendar. Home shows *Today's timetable* the same way.
- **Class page:** notes and assignments come first (they are added after the live class), then videos, recordings, materials and attendance. Join appears only from 15 minutes before a class until it ends.
- **Teachers add to a class** from its page ("Add to this class"): once the class has **ended**, a note with the key points (typed, or an AI draft from the recording approved in the web app) and an assignment for the class's students; at any time, optionally, a video from the phone with its unlock time (before/after the class, with an offset).
- **Progress** (attendance, assignments, exams…) has its own screen, *My progress*, opened from a menu row on Profile and from the Home shortcut: an attendance ring with lessons attended and active weeks, then every measure with how it is calculated.
- **Home shortcuts:** four compact pill buttons in a 2×2 grid (students: Timetable, Videos, Homework, My progress; teachers: Timetable, Videos, Grading, Doubts) — chip-sized, never a horizontal scroller.
- **Tabs only where the content is parallel lists:** Learning (Assignments · Exams · Videos) uses the fully rounded pill tab bar (`SsTabBar`). Everything else is one page of stacked sections (icon + title + count): class details (cover header → facts → videos, notes, materials, recordings, attendance; teachers see preparation and attendance first). Doubts: *Open* then *Answered & closed*. Teacher Tasks: to grade, attendance to mark, doubts to answer. Empty sections are left out (or show one quiet line); deep links such as `?tab=attendance` scroll to the section.
- **Controls:** inputs are filled (`#EFF3FA`) without a resting border and gain a 1.6 px blue border on focus; buttons are 48 px tall. The only switch left (Classes: Calendar / Upcoming / Past) is `SsSegmented`: a fully rounded track with a gradient pill that slides under the choice — never a square-edged segment.
- **States:** loading shows the turning star (still under reduced motion); empty, error, forbidden and 404 states show an illustration (or the icon on two soft discs where space is tight).
- Extra mobile tokens: `blueDeep #0A4BB0` (gradient middle), `fill #EFF3FA`, `orangeDeep #C2410C` / `orangeText #9A5B00` (AA-contrast orange for icons/text on light surfaces).

### Student-first daily journeys (2026-11-12)

The three role dashboards use `DailyPath` for three direct, role-specific tasks, with
real navigation targets and tinted `IconTile` components. Cards stack below 768px;
hover and entrance motion stay within 200ms and respect reduced-motion preferences.
`DailyPath` is web/PWA only while Flutter is parked, and is demonstrated in `/dev/ui`.

### Add classes — tuition-centre booking (2026-11-12, replaces the three-step planner)

`/sessions/new` ("Add classes" in the menu, dashboards, student list and profile) is one page
with three numbered cards that follow how the centre writes a student's week
("Hafiz · 4:30 pm · Mon Maths, Tue Science, …"), plus a summary of the student's week:

1. **Student.** Search list, or preselected via `?studentId=`; the picked card shows the student's code and tuition subjects.
2. **Dates and time.** Labelled rows (`.bk-row`: a 96 px label column, hairline between rows, stacked below 768 px):
   Starts (Today / Tomorrow / Next week / Pick a date), For (Just once / 1 week / 2 weeks / 1 month / 3 months /
   Until a date), a From → To strip (`.bk-range`) with the real dates and "N weeks · N classes" (a side becomes a
   date box when it is the admin's to choose), Days, Start time and Length. The time is in the user's zone
   (e.g. UAE time); when the student lives elsewhere a two-way switch offers "<Name>'s time" and the summary says
   what each side sees. Students always see their own time.
3. **Subjects and teachers.** "Same every day" / "Different each day" switch, then the **week list** (`.wk-list`):
   one row per chosen day with its time, subject · teacher (or the per-day `SubjectTeacherPicker`) and a status chip
   (`.wk-state`: "✓ 4 classes", "Clash", "<Name> busy", "Outside hours"). `SubjectTeacherPicker` lists only the
   student's own subjects with **Other subject…**, and only that subject's teachers with **Other teacher…**.
   "Online class?" is an optional disclosure; default is at the centre.

**Booking controls** (Add classes, admission): `.pick` chips — filled pills, 40 px (44 on phones), navy fill when
chosen; `.pick-group.multi` adds a ✓ for multi-select (admission subjects); `.cols-2/.cols-3` give even grids on
phones. `.day-pick` = round 44 px day buttons. `.pair` = two-way switch (white thumb on the fill track; not the
gradient `Segmented` component, whose `.seg` class it must not reuse).

**Clashes** sit inside their day's row (`.wk-row.bad` red edge for a teacher clash, `.warn` amber when only the
student is busy): one sentence ("Daniel is busy then. Daniel and Aisha have Class 10 — Science, 4:30–5:30 PM (2 dates).")
and `.fix` chips from `POST /sessions/batch/check` → `parts[]`: "Use <teacher>" (free teachers of that subject within
their hours; switches that day to per-day subjects), up to two "Move to <time>" (nearby free times 06:00–22:00 for
teacher and student; a per-weekday time), "Skip these N dates" (`SkipDates`).

**Summary** (`.bk-summary`, sticky on desktop): "<Name>'s classes", a 7-day strip (`.wk-strip`: booked days navy with
the subject's initials, clashing days red), the status, plan lines, notes and Create. Below 1024 px the summary follows
the form and `.bk-bar` floats above the tab bar with the status and the Create button.

Not asked: title ("Subject · Student first name"), frequency/interval, chapter, objectives.
Days with the same subject and teacher become one series; the whole plan is checked with
`POST /sessions/batch/check` ~300ms after each change and saved with `POST /sessions/batch`
in one transaction (all classes or none). Saving needs a current check, at least one date, no
teacher clash and "Book anyway" for a student-only clash. Teachers get a "New classes for you"
notification when an admin books them, so the schedule no longer has to be posted in a group chat.

### Admin home — the front desk (2026-11-12)

`/admin` greets like the other dashboards (photo, "Good evening", first name, date · centre) with **Admit student**
and **Add classes**, then: shortcut pills (`.quick-links`: Students, Teachers, Calendar, Doubts, Reports; a sideways
scroller on phones; Fees replaced Doubts on 2026-11-13); one metric strip (`.metric-strip`: Students, Teachers, Classes in
the next 7 days, open Doubts, Fees due "from N students" — each a link, hairline dividers, not five separate cards; Fees
is last so it spans both columns on phones); **Coming up**
(`.home-agenda`: classes grouped Today / Tomorrow / date, large aligned times, a subject-colour bar from the class-cover
keyword groups using the `.tone-*` palette, teacher avatar, an orange "Live now"/"Next" marker); **Needs you**
(`.home-needs`: each alert led by its count with a Review link, or "All caught up"); **Fees due** (`.home-fees`, replaced
the attendance card 2026-11-13: "To collect" total with its counts, the five most urgent students, "collected this
month") and recent activity. Attendance stays on Reports.
Below 1200 px **Needs you** comes first. "Active courses" is not shown (a tuition centre does not work in courses).

### Fees & hours (2026-11-13)

Prepaid hour packages, admin only. **Fees** (`/admin/fees`, side menu, wallet icon): a four-number strip (To collect,
Overdue, Low on hours, Collected this month — not links, so no hover) and `FeesDueList` filtered with `PillTabs`
(All / Owing / Overdue / Low on hours). `FeesDueList` (shared, also on the dashboard card): avatar, short name, "Overdue
by N days" / "Due 3 Nov" / "Paid up · renew hours", the amount (orange-deep when overdue) and an amber/red hours badge
("1.5 h left", "Out of hours", "2 h over"); each row opens `/students/{id}#fees`. **Fees & hours** panel on the student
profile (`StudentFeesPanel`, above progress): three tinted tiles (Hours left with a used/bought meter — green, amber at
≤ 2 h, red at 0 or below; Fees due; Paid), a red note when hours are used up ("Sell more hours or put the student on
hold"), one bordered row per package with its payments, **Take payment** (defaults to the amount due) and **Cancel
package** (only with no payments), and a collapsed list of the classes that used hours. Dialogs: **Sell hours** (10/20/30/50
pills or a number, price, hours count from, pay by, paid now + `MethodPicker` pills), **Take payment**, **Remove payment**
(reason required; the payment stays, struck through). Money reads "AED 4,000" (`Money.Format`, tenant currency).

### Home pattern — the same design on every home and profile (2026-11-13)

Owner request: "keep the same design in every screen". The admin front desk is now the pattern for every role, built
from shared components (do not hand-roll these again):

- `HomeGreeting` — photo (links to /profile), greeting line, first name as the h1, "Friday 13 November · caption",
  one or two pill actions (`.home-head-actions`, sharing the width on phones).
- `QuickLinks` — up to five shortcut pills (`.quick-links`), a sideways scroller on phones.
- `MetricStrip` (`MetricStrip.Item(Label, Value, Note, Icon, Href?)`) — one card of numbers split by hairlines; the
  note says what the number counts; cells with a link open where it is managed. Replaces grids of `StatCard`s
  (Reports, exam results, platform overview, Fees). Two columns on phones; an odd last cell spans both.
- `ClassAgenda` — "Coming up"/"Your classes": classes grouped by day, large time, subject-colour bar, title and
  who/how, orange "Live now"/"Next". Options: `ShowTeacher` (off for a teacher's own classes), `ShowStudentCount`,
  `MarkNext` (off for past lists such as "Attendance to mark"), `HrefFor` (e.g. the attendance tab), `ChildContent`
  for the `.next-bar` (Join / Prepare for the next class; the student sees it only while Join is open).
- `NeedsList` (`NeedsList.Item(Count, Text, Href, Warning, Action)`) — count-led rows with a pill action, or the green
  "All caught up" line (`.all-clear`).
- Layout: `.home-main` (1.7fr agenda / 1fr `.home-side`); below 1200 px "Needs you" moves first; one column on phones.

Where it is used: admin, teacher and student homes; Fees; the student profile header (`.person-head`: big avatar,
name, facts caption, "Fees & hours" + "Add classes" pills) and its "Upcoming classes". Page actions in every
`PageHeader` are pills. The teacher and student homes replaced the older next-class cover hero, stat cards and
"daily path" tiles; Progress keeps its attendance ring (a chart, not a header).

### Phone polish (2026-11-15)

Owner request: "make every screen better aligned and easier to use on the phone". Rules added (all in the
"Phone polish" block at the end of `app.css`; tokens unchanged):

- **Lists of people are rows, not cards.** Students and Teachers use `PersonRow` below 1024 px: about five people per
  screen instead of one and a half. Actions are icon buttons (`pencil`, `calendar-plus`, `clock`) with the person's name in
  the accessible label; "Active" is not repeated on every row.
- **Stacked tables are half as tall.** `table.stack-mobile` rows on phones lay labelled cells two-up (label above value);
  the first cell (title) and unlabelled action cells span the card, actions as an even row of buttons. `.kv` detail lists
  stay two columns (label | value).
- **Card and row headers with a `.btn-group`** wrap on phones: title first, then the buttons as one even full-width row.
- **Toolbars and filters fill the width.** `.list-tools` (search + status) and filter rows stretch on phones; report
  filters pair up two per row; the Classes toolbar (`.cal-tools`) is three rows on phones — List/Month + Today, the two
  filters side by side, and the period on a filled strip with 44 px arrows. Long button labels may shorten on phones with
  `.desk-text` (the full text stays in `aria-label`).
- **Tabs never cut labels.** `PillTabs` with four or more tabs scroll sideways on phones (selected tab keeps the gradient);
  `.tabs` bars fade at the end to show there is more.
- **Shell:** below 1024 px content fades into the background above the floating tab bar (no text showing around it while
  scrolling), the bar is solid white, drawer rows and the avatar are 44 px, a breadcrumb shows as one "‹ Back" link
  (the title is already in the app bar), and the install strip is one compact line (the iOS Share steps stay).
- **Participants** are one line each (avatar, name, attendance badge, 44 px remove icon); video actions are a 2×2 grid;
  progress figures are two-up.

Checked with `tests/e2e/puppeteer/mobile-audit.mjs` (every menu screen per role at 375 px, or `AUDIT_WIDTH=768/1440`):
no horizontal overflow, nothing off-screen, touch targets ≥ 44 px apart from checkboxes inside 44 px label rows and
whole-card links.

### Phone type scale, reconnect and integrations (2026-11-15, second pass)

- **Phone type scale (< 768 px):** h1 22, h2 18, h3 15, card titles 16, app-bar title 20, page subtitles 13; buttons and
  inputs 44 px with 14 px text; home greeting name 24, metric numbers 24, shortcut pills 13 px / 44 px. Body text stays
  14 px. Narrow phones (< 390 px) use 12 px page and card padding. Checked at 360 px (common Android width).
- **Reconnect** (`ReconnectModal`): Blazor Server keeps a live connection per page; phones drop it when the screen locks,
  so it must be restored before taps work. Nothing shows for the first 1.2 s (quick reconnects are invisible); then a
  light shimmer sweeps over the page that is still on screen with a thin moving bar at the top — no text, no buttons,
  taps wait. A paused session resumes by itself; when the server no longer has the session the page reloads. Only a real
  offline state shows a small card ("You seem to be offline" · Try again). The server keeps a dropped session 10 minutes.
- **Loading = skeletons.** `LoadingView` (and so every `DataView` loading state) draws shimmering placeholders shaped like
  the content (`Skeleton`: `list` rows with avatar + two lines, `page` header + cards, `block` for video) instead of a
  spinner. Classes `sk-wrap`, `sk`, `sk-row`, `sk-line`, `sk-card`; motion stops under reduced motion.
- **Settings ▸ Integrations** (admin only): three cards — Email (SMTP), SMS (Twilio / MSG91), Online payments (Razorpay /
  Stripe) — each with an icon tile, a status chip (Off · On · not tested · working · last test failed, text not colour
  alone), a plain note on what the connection is used for, the form (secret boxes say "Saved — leave blank to keep"),
  Save, and a real test (send test email / SMS, verify keys) whose answer shows as a green or red line with the time.
  Provider choice uses `Segmented`. **General** has region presets: "India · ₹ INR" / "UAE · AED" set country, currency
  and time zone together. Rupee amounts read "₹1,00,000" (Indian grouping) via `Money.Format`.

### Hours, pay, subjects and the default catalogue (2026-11-16)

Owner notes: every module shows attendance and hours attended / missed for students and teachers; each teacher and each
student has their own rate per hour; syllabi are NCERT, State (Kerala SCERT) and ICSE.

- **Teacher profile** (`/teachers/{id}`, teachers see their own at `/teacher/profile`, menu *Classes ▸ My hours & pay*):
  the same person header as the student profile, About (subjects, qualifications, experience, languages, contact),
  `HoursPanel`, the students they teach as `PersonRow`s (class · subjects, classes and hours together, next class),
  `TeacherPayPanel` and `PersonCalendar`. Teachers list rows open the profile; the pencil opens the edit dialog
  (`/admin/teachers?edit={id}` from the profile).
- **`HoursPanel`** (shared): "Attendance & hours" with a This month / All time `Segmented`; a `MetricStrip` of
  Classes attended/taught, Missed, Not marked yet / To mark, Coming up — each with hours and what it counts — then
  Present/Late/Absent/Excused badges. Student missed = absent or cancelled "student absent"; teacher missed = cancelled
  "teacher absent".
- **`PersonCalendar`** (shared): `MonthGrid` with month arrows; opens on today's classes, "Whole month" lists all.
- **Student profile** adds `HoursPanel` + "Subjects & teachers" (teacher of each subject, classes and hours) + the calendar;
  Fees shows the student's rate per hour (Set / Change) and Sell hours pre-fills price = hours × rate ("30 h × ₹400 =
  ₹12,000") until the admin types another price.
- **`TeacherPayPanel`**: rate per hour (rate history when it changed), `MetricStrip` This month · Earned to date · Paid ·
  Still to pay (or Paid in advance), a warning for classes before the first rate, payments list (struck through when
  removed, with the reason), Record payment (amount defaults to what is owed, period, `MethodPicker`), Remove (reason).
- **Cancel class** asks *why*: Teacher absent / Student absent / Holiday / Other reason (`.pick` chips, the help line says
  whose missed hours it counts as).
- **Subjects** (`/admin/subjects`, *Learning ▸ Subjects*): grouped by class · syllabus; teachers, students, classes, hours
  and fee value (hours × each student's rate); "In use" / "Whole catalogue" switch so the 226 catalogue subjects do not bury
  the ones in use.
- **Academics**: "Load default catalogue" strip and syllabus/class filters; the course form has a Syllabus field.
- **Admission**: after class + syllabus, "Subjects in Class 10 · NCERT" as `.pick` chips, then the centre's own subjects.
- **Reports**: money strip (Hours taught, Fees collected, Salary paid, Pending fees, Salary owed — admins), teacher table
  with hours taught/missed and earned, and a Subjects table. **Admin home** metric strip adds Subjects (being studied)
  and Hours taught (this month).

### Academics: edit, remove and automatic codes (2026-11-17)

- Tabs: Courses & subjects · Syllabi · Academic years · **Classes** (was "Grades"). Every course, subject, syllabus, year
  and class has **Edit** and **Remove** (`.danger-text`); Remove opens a confirm sheet ("Keep it" / "Remove") and is
  refused with a plain reason when something still uses the entry ("Still used by 8 students, 4 courses and 1 exam").
- Codes are never typed: new syllabi, courses and subjects get one automatically (NCERT / KERALA / ICSE, `NCERT-C5`,
  `NCERT-C5-MATH`, initials otherwise); edit dialogs show the code read-only (`.code-ro`). New classes need only a
  name; their order follows the class number.
- "Load default catalogue" also tidies names: classes recognised as 1–12 become "Class N" in order, and NCERT /
  Kerala / ICSE syllabi get their standard names (records keep their ids, so students stay linked).

### Phone app feel: full titles, no zoom, press feedback (2026-11-17)

- **App bar title** (< 1024 px) takes the free width and may use two lines; titles over 15 characters step down to 18 px
  and over 26 to 16 px on phones (`TitleFit` in `MainLayout`), so "Class change requests" shows in full instead of "…".
- Fields are 16 px on phones so iPhone Safari never zooms in on focus.
- Touch screens: no grey tap flash; buttons, chips, tabs and list rows give a quick press feedback (scale .97–.98,
  off with reduced motion). The bar respects the notch/status bar in the installed app (`safe-area-inset-top`).

### Show/hide passwords, 6-character passwords, student's teachers and class change requests (2026-11-17)

- **Passwords**: every password or secret box uses `PasswordInput` — an eye button (`.pw-toggle`, a real toggle with
  "Show password" / "Hide password") that keeps focus; sign-in keeps its own (static page). Change password says "At least
  6 characters"; the server needs 6+ characters and refuses the most common passwords and the person's email name.
- **Student home ▸ Your teachers** (`MyTeachersCard`, top of the side column): each teacher with the subjects they teach
  the student and "Next: Tomorrow · 4:00 PM". Opening one lists the upcoming classes (the next one highlighted) with
  **Postpone** and **Reschedule**; a sent request shows "Asked to postpone" / "Asked to move to …" with Withdraw.
- **Request dialog**: postpone = reason only (the teacher picks the time); reschedule = new date + time (in the class's
  time zone) + reason.
- **Class change requests** (`/class-requests`, Classes menu for admins and teachers; teacher home "Needs you" shows the
  count): one card per request — booked time, what the student asks, reason — with new date/time pre-filled (the
  student's suggestion, or the same time a week later), an optional note, **Accept and move** / **Keep as booked**.

### Classes must fit the hours; "Add hours"; no payment screens this phase (2026-11-17)

- **Hours rule** (owner decision): a student can be booked only for classes their prepaid hours cover. Free = hours added
  − used (attended) − already booked (not yet marked). Checked on the server for new classes, weekly plans, adding a
  student to a class and making a class longer (409 `hours_exceeded` with the numbers).
- **Add classes**: the summary card shows "Uses 7.5 h of Hiro's 195.5 h free hours" when it fits. When it does not, a red
  status — "No hours added yet" or "Only 1.5 h free · 1.5 h left, 0 h booked · these 4 classes need 3 h" — with
  **Book only the first N classes** (leaves out the later dates) and **Add hours** (to the student's Fees section);
  Create stays disabled, and the phone bar reads "Only 1.5 h free · Add hours or book fewer classes".
- Wording: packages are **added**, never "sold" (Add hours button and dialog, "5 h added." toast, empty states).
- **Online payment**: no payment screens this phase — no Payment link button, no link option in Automations, no
  payments badge. The gateway connection stays in Settings ▸ Integrations for the parents module later.

### Automations, salaries, receipts and progress reports (2026-11-17)

Owner request: automate the reminders, money follow-up and reports. Nothing automatic changes money, attendance or
classes; it reminds, alerts and prepares, and people decide.

- **Settings ▸ Automations** (`AutomationsPanel`): four cards (Classes, Fees & salaries, Reports, Teaching & alerts), each
  automation one `AutoToggle` row — title, one-line description, a real checkbox styled as a switch (`role="switch"`,
  the whole row is the label). Its settings (time, days, hours, "Also SMS the family") open in a filled box under it
  while it is on. SMS switches are disabled with an orange hint until SMS is connected; a strip on top shows Email / SMS
  / Online payments on/off. A sticky "Save automations" and a log of automated SMS (masked numbers, status, reason).
- **Fees ▸ Salaries** (`/admin/salaries`): month switcher, `MetricStrip` (hours, earned, paid, to pay now), one row per
  teacher with Earned / Paid / To pay and **Mark paid** (method picker) → opens the printable slip.
- **Receipts and slips** (`/receipts/{id}`, `/salary-slips/{teacher}/{id}`): one printable card (`.doc`), "Print or save
  as PDF" uses the browser; print styles hide the shell. Linked from each payment (Fees tab, teacher pay) and from the
  "Payment received" notification. Removed payments still print, marked.
- **Payment link** (server only this phase; no button): payments are recorded only when the gateway confirms (webhook or
  15-minute check). `/payment-done` is a public thank-you page that records nothing.
- **Progress reports** (`/progress-reports`, admins and teachers; menu Reports / Teaching): "To send / Sent / Skipped"
  `Segmented`; each card shows attended of booked, hours, hours left and average marks, a remark box, Skip / Send.
- **Holidays**: adding a holiday opens "Classes on <day>" with **Move each to the next free day** or **Cancel all**
  (reason Holiday); classes that cannot move are listed. Every holiday row also has "Classes that day".
- Menu: Fees became a group (Fees due, Salaries); Reports a group (Reports, Progress reports).

### Easy adding and one-tap attendance (2026-11-16)

Owner request: adding classes, teachers and students, and marking attendance after a class, must be very easy.

- **Quick attendance** (`QuickAttendance`, shared): one card per finished class on teacher Home ("Mark attendance", first 3)
  and Tasks (all). Each student row has a Present / Late / Absent segmented control (`.qa-mark`, coloured green / amber /
  red when chosen, 44 px on phones). One student: the tap saves. Several: "All present", change who was not, "Save
  attendance". The card leaves the list once saved. "Open" goes to the class page for Excused, notes and corrections.
- **Add a class** (Academics, primary button): choose Class + Syllabus → that class's usual subjects appear as ticked
  `.pick` chips (existing ones shown fixed), add a custom subject by typing + Enter, one save ("Create with 6 subjects" /
  "Add 1 subject" / "Nothing new to add"). "Other kind of course…" opens the full course form.
- **Add teacher**: name, email, phone, *Pay per hour* (optional), then subjects as chips by name — a tap gives the subject
  for every class, a partly-given subject shows `aria-pressed="mixed"` with "· 1 of 22". A "Find a subject" box appears
  when there are more than 12. "Only some classes?" (one class at a time) and "More details" (code, time zone,
  qualifications, experience, about) are collapsed `details.planner-optional`; editing opens More details when it has data.
- **Admit a student**: 1 Student (name, email, phone, *Fee per hour*; code and time zone pre-filled under a collapsed line),
  2 Class, 3 Tuition subjects. Parent / guardian, Last school result and Joining & goal are collapsed cards
  (`details.admit-optional`, "+" / "−").
- Rates typed while adding are saved right after the person is created (`PUT teachers/{id}/pay/rate`,
  `PUT students/{id}/rate`); if that second call fails the person still exists and the error toast says so.

### One screen per job, scoped by role (2026-11-13)

Admins and teachers use the same page for the same job; the role only hides actions. `/teacher/students` is the admin
Students table (no Edit / Admit, only the students they teach); `/students/{id}` is one profile for both (Fees, Tuition
subjects editing and parent phone numbers are admin-only). Teacher subject pickers list only the teacher's own subjects
(`?mine=true`) and show "No subjects on your profile yet. Ask the admin…" when empty.

