---
name: responsive-ui-implementation
description: Implement and verify Student Sync web layouts at mobile (375), tablet (768) and desktop (1440) widths — breakpoints, overflow, stacked tables, dialogs, touch targets and the side-menu drawer. Use for every page or component change that renders HTML/CSS, and for Flutter screens on small devices.
---

# Responsive UI implementation (Student Sync)

> **Flutter is parked this phase (owner decision 2026-11-04).** Student Sync currently ships
> the web portals and the installable PWA only. Treat every Flutter/mobile-app instruction in
> this skill as suspended: do not build, test, mirror or verify anything under
> `mobile/student_sync` until the owner re-enables the app.

## Purpose and triggers

Every web page must work on phones, tablets and desktops with the same grouped side menu
(docked ≥ 1024 px, drawer below). Use this skill whenever you add or change a page, section,
grid, table, form, dialog or any CSS, and when a user reports a layout problem ("cut off",
"overflows", "button under the bar"). Pair with `frontend-design`, `ui-ux-design-system`,
`puppeteer-browser-testing` (screenshots) and `accessibility-audit` (touch targets, focus).

## Inspect first (real files)

- `DESIGN.md` §4 (breakpoint table, shell behaviour) and §3 (controls, radius).
- `src/StudentSync.Web/wwwroot/css/app.css`:
  - shell: `.sidebar` (line 63, fixed 248 px navy), the drawer rule `@media (max-width: 1023px)` (line 95:
    sidebar slides in with `.shell.nav-open`), `.topbar` (line 90; 64 px high below 768 px, line 103);
  - grids: `.grid-2/.grid-3/.grid-4/.grid-main` collapse at 1199 px and 767 px (lines 53–54);
  - forms: `.form-grid` → one column below 768 px (line 239); `.choice-cards` 2-up below 1200 px (249);
  - other page-specific rules at 1099/899/479 px (teacher dashboard `.t-*`, student `.st-*`, `.kv`, `.bar-row`).
  - reduced motion (line 402).
- `src/StudentSync.Web/Components/Layout/MainLayout.razor` — `aria-expanded` menu button (`.menu-btn`,
  line 44), `_navOpen`, closing the drawer on link click (line 107); `[dir="rtl"]` mirrored rules in CSS.
- Shared pieces with built-in responsive behaviour: `Components/Shared/MonthGrid.razor` (compact month with
  dots on phones), `ShortcutGrid.razor` (2×2 pills, never a scroller), `Pager.razor`, `Modal.razor`,
  `SessionItem.razor`, `PillTabs.razor`/`Segmented.razor`.
- Existing automated checks: `tests/e2e/tests/journeys.spec.ts` "Responsive layout @responsive" block
  (horizontal-overflow assertion at 375 px for a page list) and `tests/e2e/playwright.config.ts`
  (projects `desktop` 1440×900 and `mobile` 375×812).
- Web < 1024 px: the floating tab bar (`.tabbar`, `MainLayout.razor`) overlays the bottom 100 px + safe area;
  `.shell.has-tabs .content` already pads for it — check sticky footers, dialogs and FABs stay above it.
- Flutter: `mobile/student_sync/lib/features/shell/role_shell.dart` (floating bottom bar; content must
  not sit under it — a real bug found on device), `lib/core/widgets/states.dart`.

## Rules that must not be broken

- **Breakpoints:** < 768 px mobile (single column, tables → stacked cards), 768–1023 px tablet (drawer,
  2-column grids), ≥ 1024 px desktop (docked sidebar, content max-width 1280 px). Use the existing
  media-query widths; do not invent new breakpoints without a DESIGN.md §4 update.
- **No horizontal page overflow at 375 px** (`document.documentElement.scrollWidth <= clientWidth`).
  Long text wraps (`overflow-wrap: anywhere` on user content), images/videos are `max-width: 100%`,
  tables that cannot stack get an explicit scrolling wrapper, never the page.
- **Touch targets ≥ 44×44 px** on mobile; buttons 48 px (`.btn-sm` 36 px only in dense desktop tables).
- **Drawer behaviour:** menu button visible only below 1024 px (no stray menu button on desktop — a
  regression that was fixed before), closes when a link is chosen, trap/escape handled by the existing shell.
- **Dialogs** (`Modal`, 28 px radius) fit a 375×812 viewport: scroll inside, actions stay reachable.
- **Grids** use `minmax(0, 1fr)` so children can shrink (pattern already in `app.css`).
- **RTL:** anything positioned with left/right must use logical properties or a `[dir="rtl"]` rule
  (Arabic layout, DESIGN.md §2).
- Time strings, badges and pills must not break mid-token; counts (e.g. "99+") stay inside their dot.

## Workflow

1. Read the page's markup and list every region that is a grid, table, form, media, dialog or sticky bar.
2. For each region decide the behaviour per breakpoint (stack / 2-up / scroll-inside / hide secondary
   column) using the existing utility classes first (`.grid-*`, `.form-grid`, `.kv`, `.card.tight`).
3. Add CSS in `app.css` next to the page's existing block (search for the page prefix such as `.t-` or
   `.st-`); reuse the existing media-query widths.
4. Run the page at 375, 768, 1024 (drawer ↔ docked boundary) and 1440 px with Puppeteer; screenshot each,
   plus the open drawer at 375 px and any dialog open at 375 px.
5. Evaluate overflow (`scrollWidth - clientWidth`) at 375 px and fix anything > 0.
6. Add the page to the "Responsive layout @responsive" list in `journeys.spec.ts` so the check stays.
7. Flutter: run the screen on a small simulator (iPhone SE class) and a large one; check content above the
   floating bottom bar and inside safe areas; text scale 1.3 still readable.

## Security / permission notes

Responsive variants must show the same data as the desktop variant — never render a "mobile summary" that
bypasses the role scoping of the full view, and never move secrets (meeting links, file links) into
attributes that print at other widths.

## Required checks

- Puppeteer screenshots: 375 / 768 / 1440 px (and 1024 px when the shell or sidebar is involved), each
  reachable state, drawer open and closed, dialogs open; saved where `puppeteer-browser-testing` specifies.
- Overflow assertion at 375 px passes; `npx playwright test --project=mobile` (in `tests/e2e`) passes.
- axe at 375 px for the page (`accessibility-audit`), including target-size issues.
- No console errors or failed requests at any width.

## Acceptance criteria

- No horizontal overflow, clipped text, overlapped controls or unreachable actions at 375/768/1440 px.
- Side menu docks at ≥ 1024 px and is a working drawer below; nothing else navigates.
- Tables stack or scroll inside their card on phones; forms are single-column below 768 px.
- Touch targets and focus rings meet DESIGN.md §6.

## Completion checklist (report these)

- [ ] Widths tested and screenshot paths per width/state.
- [ ] Overflow value at 375 px (must be ≤ 0) and the e2e list entry added.
- [ ] CSS added (file/lines) and which existing utilities were reused.
- [ ] Flutter devices checked, if applicable; anything not verified.
