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

Navigation is a single side menu with collapsible groups (for example *Classes ▸ Timetable / Schedule a class / Pre-recorded videos*), defined per role in `Navigation.For` (`Services/UiServices.cs`). The group containing the current page opens automatically, and exactly one link is marked current (the longest matching path). There is no top navigation.

**Phones and tablets (< 1024 px) use the Flutter app's shell (owner request 2026-11-04: the PWA must match the mobile app).** `Navigation.Tabs(role)` defines the same four tabs as `role_shell.dart` — student *Home · Subjects · Learning · Doubts*, teacher *Home · Classes · Students · Tasks*, admin *Home · Students · Classes · Reports* — rendered by `MainLayout` as a floating bar (16 px side margins, 12 px above the home indicator via `env(safe-area-inset-bottom)`, 72 px high, 28 px radius, frosted white, floating shadow). The selected tab is a navy pill with icon + label and grows to twice the width; the others are icon-only fill tiles with tooltips. A tab also owns its child routes (for highlighting only, e.g. `/timetable` and class pages under *Subjects*; `/assignments`, `/exams`, `/videos`, `/learning-plan` under *Learning*). Everything not in the four tabs stays reachable from the drawer (menu button). Content keeps 100 px + safe area of bottom padding so the last card and the toasts clear the bar; a page's single primary action may float above the bar with `.fab-mobile` (used by Doubt Centre's *Ask a doubt*, like the mobile FAB). On desktop (≥ 1024 px) nothing changes: docked side menu, no tab bar.

- **Student:** Dashboard · Subjects (`/subjects`) · Timetable (`/timetable`) · Learning (`/learning`, one page with pill tabs Assignments · Exams · Videos; `/assignments`, `/exams`, `/videos` keep working) · Doubt Centre · AI assistant · *Communication ▸* Messages, Announcements. No Library (notes belong to each class page; `/library` still works for existing links) and no Progress item (it opens from Profile → *My progress*).
- **Teacher:** Dashboard · *Classes ▸* Timetable (`/sessions` calendar), Schedule a class, Pre-recorded videos, Availability · *Teaching ▸* Students, Tasks (`/teacher/tasks`), Assignments, Exams, Question bank, Doubt Centre · *Communication ▸*.
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
| `BrandSpinner`, `LoadingView` | `BrandSpinner`, `LoadingView` | The logo's book with its star turning and pulsing (still under reduced motion). Used by `DataView`, route authorisation and redirects |
| `StateView` | `EmptyState`/`ErrorState`/`ForbiddenState` | Illustration (`Illustrations.*`: empty, search, offline, server_error, access_denied, not_found, all_done, questions, mailbox) + title + message + actions; `Compact` shows the icon on two soft discs |
| `DataView` | `AsyncView` | Every data region: loading → error (offline/server/403/404 illustration, Retry) → empty (`EmptyIllustration`, or derived from `EmptyIcon`) → content. `Compact` for tight cards |
| `SessionItem` / SessionCard | `SessionCard` | Time in viewer's zone (+ organiser zone when different), type badge, Join |
| `Modal`, `Toasts` | dialogs, snack bar | Focus-trapped dialogs (28 px radius; bottom sheet on phones); toasts announced politely |
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
