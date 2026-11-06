---
name: frontend-design
description: Plan and build any new or changed Student Sync screen, page section or component (web Blazor or Flutter) so it is task-oriented, on-brand and production-ready, not a generic AI dashboard. Use before writing any UI markup.
---

# Frontend design (Student Sync)

> **Flutter is parked this phase (owner decision 2026-11-04).** Student Sync currently ships
> the web portals and the installable PWA only. Treat every Flutter/mobile-app instruction in
> this skill as suspended: do not build, test, mirror or verify anything under
> `mobile/student_sync` until the owner re-enables the app.

## Purpose and triggers

Use this skill **before** writing or changing any user-facing screen, page section, dialog,
card, form or Flutter screen. Triggers: "add a page/screen/tab/panel", "redesign", "make this
look better", "new dashboard widget", "add a form", any task in CLAUDE.md addendum sections
C–J that has a UI, and any Flutter feature under `mobile/student_sync/lib/features/`.

It is always used together with `ui-ux-design-system` (tokens and components) and
`responsive-ui-implementation` (breakpoints). For browser proof use `puppeteer-browser-testing`;
for styling changes also `visual-regression-qa`; for forms also `accessibility-audit`.

## Inspect first (real files)

- `DESIGN.md` — the single source of truth (brand, type scale, spacing, radius, shell, components, voice).
- `src/StudentSync.Web/wwwroot/css/app.css` — `:root` tokens and every page/utility class (870 lines; grep before adding a class).
- `src/StudentSync.Web/Components/Shared/` — the shared components (`PageHeader`, `DataView`, `StateView`,
  `SectionHeader`, `HeroCard`, `IconTile`, `Badge`, `TagPill`, `ListGroup`/`ListRow`, `Segmented`, `PillTabs`,
  `Modal`, `Toasts`, `StatCard`, `MetricTile`, `ShortcutGrid`, `InlineEmpty`, `Avatar`, `ClassCover`, `SessionItem`).
- `src/StudentSync.Web/Services/UiServices.cs` — `Navigation.For(role)` (side-menu groups), `StatusStyles`,
  `Illustrations`, `ClassCovers`, `I18n`. A new page must be reachable from a menu group here.
- `src/StudentSync.Web/Components/Pages/DevUiKit.razor` — live gallery at `/dev/ui` (Development only); open it to
  see the real rendering of every component before composing a page.
- A sibling page of the same role as a pattern, e.g. `Components/Pages/Teacher/TeacherDashboard.razor`,
  `Components/Pages/Admin/StudentProfile.razor`, `Components/Pages/Student/SubjectDetail.razor`,
  `Components/Pages/Learning/SessionHub.razor` (+ `Hub/*Tab.razor`).
- Flutter: `mobile/student_sync/lib/core/theme/{tokens,app_theme,status_styles}.dart`,
  `lib/core/widgets/` (`async_view`, `states`, `session_card`, `class_cover`, `common`), `lib/features/shell/role_shell.dart`.
- Product rules for the screen: the matching section of `CLAUDE.md` (addendum A–J) and
  `Student_Sync_Institute_Workflow_Master_Prompt.md` §15 (dashboards) / §17 (UX expectations).

## Rules that must not be broken

- **One brand.** Only tokens from DESIGN.md §1–3. No new colours, fonts, radii, shadows or spacing values.
  Orange is a restrained accent; the gradient appears only where DESIGN.md §3 allows.
- **Grouped side menu, never top navigation** (DESIGN.md §4; CLAUDE.md §5). Below 1024 px the web shell
  mirrors the mobile app: page title in the app bar, bell + avatar, and the floating four-tab bar from
  `Navigation.Tabs` (same tabs as `role_shell.dart`); the side menu is the drawer. Do not add a second
  bar or extra tabs; a page's single primary action may use `.fab-mobile` to float above the bar.
- **Page anatomy:** `PageHeader` (title, subtitle, one primary action) → optional filter bar → content cards.
  One primary action per page. Headings ≤ 28 px, sentence case.
- **Every data region goes through `DataView`** (loading → error → empty → content) and every whole-page
  state through `StateView`; use `InlineEmpty` for a quiet empty line inside a section. Never ship a blank
  region, a spinner with no label, or a swallowed error.
- **No fake UI.** A button that has no working API is not rendered, or is disabled with an explanation,
  or is labelled "not yet available". Figures come from the API (`StatCard`/`MetricTile` only show real data;
  `MetricTile` must explain how a number is calculated — no invented percentages, CLAUDE.md addendum J).
- **Hiding is not authorization.** Role/permission checks in the UI (`AuthorizeView`, menu entries) are
  convenience; the API enforces access. Never render another student's data.
- **Voice** (DESIGN.md §8): plain, specific, says what happened and what to do next; never claims an
  external action succeeded unless the API confirmed it.
- Do not redesign unrelated screens or remove existing features (master prompt §17).

## Workflow

1. Write the user outcome in one sentence per role ("A teacher can publish a class report from the
   class page without leaving it"). List the states: loading, empty, error, forbidden, success, disabled.
2. Map the content to the page anatomy: header → primary action → sections in task order (what the
   person must do next first; history and reference last). Sketch it as a bullet outline, not pixels.
3. Pick shared components for every element. If none fits, extend a shared component with a slot or
   parameter; create a new shared component only when it will be used by ≥ 2 pages, and add it to `/dev/ui`.
4. Decide responsive behaviour per section with `responsive-ui-implementation` (tables → stacked cards
   below 768 px, grids collapse via `.grid-2/3/4`, drawer below 1024 px).
5. Wire data through a typed `ApiClient` call (`src/StudentSync.Web/Services/ApiClient.cs`); handle
   `ApiException` via `DataView.Error`. Flutter: `AsyncView` with the repository/API layer in `lib/core/api`.
6. Add the menu entry in `Navigation.For` (and its `I18n` strings for Arabic/Malayalam shell labels).
7. Implement, then run the checks below. Compare the screenshots with a sibling page for consistency
   (card padding, section headers, badge styles, button heights 48 px / `.btn-sm` 36 px).
8. Update `DESIGN.md` §4/§5 if a new navigation item, page pattern or shared component was introduced.

## Required checks

- Puppeteer (`puppeteer-browser-testing`): screenshots at 375, 768 and 1440 px of every new state you can
  reach with demo data; no console errors; no failed requests; `scrollWidth <= clientWidth` at 375 px.
- axe (`accessibility-audit`): no serious/critical WCAG 2.1 AA violations on the new page; add it to the
  page lists in `tests/e2e/tests/journeys.spec.ts` ("Responsive layout" and "Accessibility" blocks).
- Styling changes to shared components: `visual-regression-qa`.
- Flutter: `flutter analyze` clean, widget test for new state handling, simulator screenshot.

## Acceptance criteria

- The screen answers "what do I need to do next?" at first glance for its role.
- Only DESIGN.md tokens and shared components are used; nothing visually new was invented.
- Loading, empty, error, forbidden and success states exist and were seen in the browser.
- Reachable from the grouped side menu (web) or the correct tab (Flutter); active link highlighted.
- Works at 375/768/1440 px with no overflow; axe clean; no console/network errors.

## Completion checklist (report these)

- [ ] Outcome sentence and state list written before coding.
- [ ] Files touched (pages, shared components, `UiServices.cs`, `app.css`, DESIGN.md).
- [ ] Screenshot paths per viewport and state; console/network results.
- [ ] axe result; overflow result at 375 px.
- [ ] Anything not verified (e.g. a state unreachable with demo data) stated explicitly.
