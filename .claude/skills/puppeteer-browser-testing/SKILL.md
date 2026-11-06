---
name: puppeteer-browser-testing
description: Use for any browser-visible change or workflow in the Student Sync web app — run real-browser checks with puppeteer-core and system Chrome, capture screenshots at 375/768/1440, and report console errors and failed requests.
---

# Puppeteer browser testing

> **Flutter is parked this phase (owner decision 2026-11-04).** Student Sync currently ships
> the web portals and the installable PWA only. Treat every Flutter/mobile-app instruction in
> this skill as suspended: do not build, test, mirror or verify anything under
> `mobile/student_sync` until the owner re-enables the app.

## Purpose and triggers
Puppeteer is the required browser-automation path (CLAUDE.md section O4). Use this skill whenever a task touches a
Blazor page/component, navigation, a form, a role-specific journey, or anything the user will see in a browser.
It complements, and does not replace, unit, integration, API and authorization tests (`student-sync-testing`).

The existing Playwright suite (`tests/e2e/tests/*.spec.ts`, 43 passing tests, axe scans, visual baselines) is the
regression suite and stays in place. Do not delete or migrate it without owner approval. New ad hoc checks and
screenshots for a change are done with Puppeteer.

## Inspect first
- `tests/e2e/puppeteer/README.md`, `browser.mjs` (launch, viewports, `signIn`, collectors, `screenshot`), `smoke.mjs`.
- `tests/e2e/tests/helpers.ts` (demo users, how the Playwright suite signs in) and `tests/e2e/playwright.config.ts`.
- `DEVELOPMENT.md` for starting the stack: `docker compose up -d sqlserver mailpit`, `dotnet run --project
  src/StudentSync.Api -- seed-demo`, API on :5080, web on :5090.
- `DESIGN.md` for breakpoints (side menu docks at 1024 px) and the states every screen must show.
- The page under test under `src/StudentSync.Web/Components/Pages/` to know its route and labels.

## Rules
- Browser: `puppeteer-core` 25.x (dev dependency of `tests/e2e` only) driving the system Chrome at
  `/Applications/Google Chrome.app/Contents/MacOS/Google Chrome`; override with `PUPPETEER_EXECUTABLE_PATH`.
  No browser download (disk is limited) and nothing added to production projects.
- Credentials come from `E2E_PASSWORD` or `DEMO_PASSWORD` in the git-ignored repo `.env`. Never hard-code
  passwords, tokens or real people. Demo accounts are fictional `*@horizon.example.test` users (admin, amira,
  priya, aisha, fatima; `admin@northbridge.example.test` is the other institution for isolation checks).
- Screenshots go to `tests/e2e/puppeteer/screenshots/` (git-ignored). Copy reviewed images to `mobile/student_sync/docs/screenshots/` (mobile) or a new `docs/screenshots/` folder
  only when they document a finished feature (`docs/screenshots/` does not exist yet; create it when first needed). Screenshots must contain demo data only.
- Checks must be deterministic and repeatable; create test data through the app/API and clean up or use the
  idempotent demo seed. Never run against a production URL.
- Do not trigger `alert`/`confirm` dialogs; prefer the app's own modal components.

## Workflow
1. Start the stack (`docker compose up -d sqlserver mailpit`; `dotnet run --project src/StudentSync.Api`;
   `dotnet run --project src/StudentSync.Web`) and seed demo data. Confirm `/health` on both.
2. `cd tests/e2e && npm ci` (once). Prove the browser works: `BASE_URL=none npm run puppeteer:smoke`.
3. Write `tests/e2e/puppeteer/<flow>.mjs` importing `launch`, `newPage`, `signIn`, `users`, `viewports`,
   `screenshot` from `./browser.mjs`. Keep one flow per file; name steps clearly.
4. Drive the journey as the right role: sign in, navigate through the grouped side menu, fill forms, submit, and
   assert on real rendered text/URLs, not only on absence of errors.
5. Repeat at `viewports.desktop` (1440) and `viewports.mobile` (375); add `tablet` (768) when the layout changes
   between them (tables, dialogs, side menu drawer). Open the drawer on mobile and check for horizontal overflow
   (`document.documentElement.scrollWidth <= innerWidth`).
6. Collect `consoleErrors` and `failedRequests` from `newPage()`; every entry must be explained or fixed.
7. Check loading, empty, error, success and permission-denied states where the feature has them; check keyboard
   focus order on new forms (Tab through, visible focus ring).
8. Run `node puppeteer/<flow>.mjs`; look at every screenshot yourself before claiming a screen looks right.
9. For role boundaries, repeat the sensitive URL as a student/other teacher/other institution and confirm the
   app shows the forbidden/not-found state, never the data.
10. When the change touches a journey already covered by Playwright, also run `npx playwright test`.

## Acceptance criteria
- The flow passes at desktop and mobile with zero unexplained console errors or failed requests.
- Screenshots at each viewport were captured and inspected; defects found were fixed and re-captured.
- Unauthorized roles cannot reach the data in the browser (and the API test for it exists).
- The script runs again from a clean start without manual steps beyond the documented ones.

## Completion checklist (report exactly)
- Commands run and their real output (`npm run puppeteer:smoke`, `node puppeteer/<flow>.mjs`, Playwright if run).
- Viewports exercised; screenshot paths; what was visually inspected.
- Console errors / failed requests seen and their resolution.
- What was NOT exercised in a browser (e.g. system file pickers, email delivery) and why.
- Related: `student-sync-testing`, `visual-regression-qa`, `accessibility-audit`, `responsive-ui-implementation`.
