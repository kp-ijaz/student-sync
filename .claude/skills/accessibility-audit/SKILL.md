---
name: accessibility-audit
description: Audit and fix accessibility of Student Sync screens and forms (semantics, labels, keyboard, focus order, contrast, live regions, touch targets) against WCAG 2.1/2.2 AA using the existing axe setup. Use for every new or changed form, interactive control, dialog, table, navigation or status display, web and Flutter.
---

# Accessibility audit (Student Sync)

> **Flutter is parked this phase (owner decision 2026-11-04).** Student Sync currently ships
> the web portals and the installable PWA only. Treat every Flutter/mobile-app instruction in
> this skill as suspended: do not build, test, mirror or verify anything under
> `mobile/student_sync` until the owner re-enables the app.

## Purpose and triggers

Student Sync is used by children and by teachers under time pressure; everything must work with a
keyboard, a screen reader and at AA contrast. Use this skill for: any form (student onboarding, teacher
assignment, scheduling, attendance, class report, assignment/exam authoring, document upload), any custom
control (`Segmented`, `PillTabs`, `Modal`, calendars, attendance toggles, exam timers), status/colour
changes, navigation changes, and before reporting any UI task complete. Pair with `frontend-design`,
`responsive-ui-implementation` and `puppeteer-browser-testing`.

## Inspect first (real files)

- `DESIGN.md` §6 (accessibility rules), §5 (component ARIA notes: `PillTabs` tablist/tabpanel, `Segmented`
  native radios, `Modal` focus trap, `Toasts` polite live region, `MetricTile` details/summary).
- Existing axe tests: `tests/e2e/tests/journeys.spec.ts` "Accessibility (axe, WCAG 2.1 AA)" block —
  `AxeBuilder` with tags `wcag2a, wcag2aa, wcag21aa`, failing on `serious`/`critical`; page list there is
  where new pages are registered. Dependency: `@axe-core/playwright` in `tests/e2e/package.json`.
- Keyboard handling for tabs: `src/StudentSync.Web/wwwroot/js/app.js` (←/→/Home/End, RTL aware).
- Shell semantics: `src/StudentSync.Web/Components/Layout/MainLayout.razor` (`<aside aria-label="Main
  navigation">`, `aria-expanded`/`aria-controls` on group buttons, `aria-current="page"` on the active link,
  labelled icon buttons with unread counts in the label, `Aria.Bool` helper).
- Focus and contrast tokens: `src/StudentSync.Web/wwwroot/css/app.css` (`:focus-visible` rules, text
  colours `--ss-text`/`--ss-text-2`, orange text variants, reduced-motion rule line 402).
- Shared components: `Components/Shared/Modal.razor`, `Toasts.razor`, `PillTabs.razor`, `Segmented.razor`,
  `Badge.razor` (text + dot, never colour alone), `LocalTime.razor` (time zone shown), `DataView.razor`.
- Flutter: `Semantics` usage in `lib/core/widgets/` and `lib/features/shell/role_shell.dart` (tooltips and
  screen-reader labels on icon-only tabs).
- Known past findings (do not reintroduce): secondary text in Slate below AA on Soft Background; empty
  `aria-pressed`/`aria-selected` values; calendar ARIA/contrast issues; a button under the floating bar.

## Rules that must not be broken

- WCAG 2.2 AA: text contrast ≥ 4.5:1, UI boundaries and focus indicators ≥ 3:1 (DESIGN.md §6).
- Every input has a `<label>` (or `aria-label` for icon-only controls); errors linked with
  `aria-describedby`; required fields marked in text, not only with colour or `*`.
- Visible focus on every interactive element: 2 px blue outline + 2 px offset; never `outline: none`.
- Status is never colour alone: badges carry text; attendance/ledger/diagnostic states use text + colour.
- Dialogs: `role="dialog"`, `aria-labelledby`, focus moved in on open and restored on close, Escape closes.
- Dynamic results (toast, "Attendance saved for 5 students", exam countdown warnings) are announced via a
  polite live region; a timer must also be perceivable without colour and must not be the only cue.
- Touch targets ≥ 44×44 px on mobile; tab order follows visual order; no keyboard traps.
- Times include the time-zone when it differs from the viewer's; icons have `aria-hidden` when decorative.
- Motion respects `prefers-reduced-motion`.

## Workflow

1. Read the page markup; list every interactive element, form field, status indicator and dynamic region.
2. Static pass: labels, headings order (one `h1` per page from `PageHeader`), landmarks, `alt` text on
   covers/photos (`Avatar Decorative`), `aria-*` values rendered non-empty (use `Aria.Bool`).
3. Keyboard pass in the browser (Puppeteer `page.keyboard.press("Tab")` loop, log `document.activeElement`):
   reach every control, operate it (Enter/Space/arrows), open and close dialogs, no trap, focus visible in
   the screenshot.
4. Run axe on the page at 1440 px and 375 px with the same tags as `journeys.spec.ts`; treat `serious`/
   `critical` as blocking and review `moderate` (fix when cheap).
5. Contrast check any new colour pairing against the token table (DESIGN.md §1 lists safe pairs).
6. Fix at the source: shared component or token first, page second. Re-run axe and keyboard pass.
7. Register the page in the axe page list in `tests/e2e/tests/journeys.spec.ts`; run that block.
8. Flutter: `Semantics` labels on icon-only controls, text scale 1.3 check, `flutter test` widget tests
   with semantics assertions where a control was added.

## Security / permission notes

Accessible names and live-region text must not reveal data the role may not see (e.g. a screen-reader
label listing another student's name on a group session a teacher is not assigned to). "Access denied"
states use the same wording whether the record exists or not.

## Required checks

- axe (WCAG 2.1 AA tags) clean of serious/critical on each changed page at desktop and 375 px.
- Keyboard walkthrough recorded (list of focus stops) and a screenshot showing a visible focus ring.
- Contrast of any new text/background pair stated with its ratio.
- `npx playwright test -g "Accessibility"` (in `tests/e2e`) passes with the page added.

## Acceptance criteria

- Every control is labelled, reachable and operable by keyboard; focus order is logical; no trap.
- No serious/critical axe violations; contrast meets AA; status never colour-only.
- Dialogs and toasts follow the shared component patterns; dynamic changes are announced.
- Mobile touch targets ≥ 44 px.

## Completion checklist (report these)

- [ ] Pages audited, axe results (ids and counts) before/after.
- [ ] Keyboard pass summary and focus screenshot path.
- [ ] Fixes made (component/token/page) and the e2e list entry.
- [ ] Anything not verified (e.g. real screen-reader run not performed).
