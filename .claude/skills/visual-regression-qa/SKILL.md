---
name: visual-regression-qa
description: Capture comparable before/after screenshots of Student Sync screens at 375/768/1440 px, compare them, and maintain the existing Playwright pixel baselines so styling or shared-component changes do not silently break other screens. Use for any visual redesign, CSS/token change, shared-component change, or when asked whether the UI still looks the same.
---

# Visual regression QA (Student Sync)

> **Flutter is parked this phase (owner decision 2026-11-04).** Student Sync currently ships
> the web portals and the installable PWA only. Treat every Flutter/mobile-app instruction in
> this skill as suspended: do not build, test, mirror or verify anything under
> `mobile/student_sync` until the owner re-enables the app.

## Purpose and triggers

Shared components and tokens are used by every role's pages, so a styling change can break a screen
nobody opened. Use this skill whenever: `app.css`, `tokens.dart`, anything in
`src/StudentSync.Web/Components/Shared/`, `Components/Layout/`, `DevUiKit.razor`, fonts, icons, covers
or illustrations change; a page is restyled; or the sign-in/offline pages are touched. Pair with
`ui-ux-design-system` (what the change should be) and `puppeteer-browser-testing` (how to drive the browser).

## What already exists — reuse it, do not add a service

- **Playwright pixel baselines** in `tests/e2e/tests/visual.spec.ts` with snapshots in
  `tests/e2e/tests/visual.spec.ts-snapshots/` (`login-{desktop,mobile}-darwin.png`,
  `offline-{desktop,mobile}-darwin.png`). They cover only screens without live data (sign-in, offline),
  use `maxDiffPixelRatio: 0.01`, are rendered on macOS (`-darwin` suffix) and are **skipped in Linux CI**.
  Update them only intentionally: `npx playwright test visual --update-snapshots` (run from `tests/e2e`),
  then look at the new PNGs before accepting.
- **Viewports** come from `tests/e2e/playwright.config.ts`: `desktop` 1440×900 and `mobile` 375×812
  (`@responsive` tag). Tablet (768 px) is captured ad hoc with Puppeteer.
- **Data-driven screens** (dashboards, timetables, student 360) have no pixel baselines because demo
  data changes; they are compared by eye from before/after screenshots at the same viewport, route,
  signed-in user and state.
- Demo accounts and password for signing in: `tests/e2e/tests/helpers.ts` (`users`, `demoPassword()` from
  `.env` `DEMO_PASSWORD` / `E2E_PASSWORD`); fictional data only (`seed-demo`).
- Do not add Percy/Chromatic/another screenshot service or a second baseline folder.

## Inspect first (real files)

- `DESIGN.md` (expected look), `tests/e2e/tests/visual.spec.ts`, `visual.spec.ts-snapshots/`,
  `tests/e2e/playwright.config.ts`, `tests/e2e/tests/helpers.ts`.
- The list of pages affected by the change: grep the changed class/component across
  `src/StudentSync.Web/Components` and list each route; `/dev/ui` for shared components.
- Screenshot location and driver: `.claude/skills/puppeteer-browser-testing/SKILL.md`.
- Flutter: `mobile/student_sync/docs/screenshots/` holds the last simulator walkthrough images
  (captured with `mobile/student_sync/scripts/capture_screenshots.sh <simulator-udid>`).

## Rules that must not be broken

- A baseline update is a deliberate decision: state **why** the pixels changed; never update to make a
  failing test pass without looking at the diff image in `tests/e2e/test-results/`.
- Before/after pairs must be comparable: same route, viewport, user, data state, fonts loaded
  (`networkidle`), animations finished (reduced motion is honoured; wait for transitions), toasts dismissed.
- Compare the **set of affected pages**, not only the page you were working on; include one page per role
  that uses the changed component, plus `/dev/ui`.
- Screenshots never contain real personal data (demo data only) and are not committed unless they are
  Playwright baselines or the documented Flutter walkthrough set.
- Pixel baselines stay limited to static screens; do not add baselines for data-driven pages.

## Workflow

1. From the diff, list changed tokens/components/CSS blocks and grep for every consumer; write the page
   list (route + role + state) to capture.
2. **Before** capture (on the unchanged build, e.g. stash or a second checkout, or the last known-good
   screenshots if they exist): Puppeteer screenshots at 375, 768, 1440 px for each listed page and state.
3. Apply the change; build; restart the web app; **after** capture with the identical script.
4. Compare: run `npx playwright test visual` (in `tests/e2e`) for the pixel baselines; for the rest, open
   before/after side by side (or compute a pixel diff with Puppeteer's PNG buffers) and note every
   difference as intended or regression.
5. Fix regressions at the source (component/token), re-capture only the affected pages.
6. If sign-in/offline changed intentionally: `npx playwright test visual --update-snapshots`, inspect the
   four new PNGs, and mention the update in the report.
7. Flutter styling changes: re-run the simulator walkthrough script for the affected screens and compare
   with `mobile/student_sync/docs/screenshots/`.

## Security / permission notes

Capture only with demo accounts; ensure screenshots do not include temporary passwords
(`TempPasswordDialog`), signed file URLs or tokens visible in the address bar or page text.

## Required checks

- `npx playwright test visual` result (pass, or intentionally updated with reasons).
- Before/after screenshots at 375/768/1440 px for every affected page, with a one-line verdict each.
- `/dev/ui` before/after when a shared component changed.
- No console errors during captures.

## Acceptance criteria

- Every consumer of a changed token/component was captured and reviewed; only intended differences remain.
- Pixel baselines pass or were updated deliberately with inspected images.
- No new screenshot service or baseline folder was introduced.

## Completion checklist (report these)

- [ ] Changed tokens/components and the affected-page list.
- [ ] Screenshot paths (before/after, per viewport) and verdicts.
- [ ] Playwright visual result; baselines updated? why?
- [ ] Regressions found and fixed; anything not captured (e.g. state unreachable with demo data).
