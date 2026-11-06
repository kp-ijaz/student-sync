# Puppeteer browser checks

Real-browser checks for Student Sync using `puppeteer-core` and the system Chrome (no browser download; dev-only
dependency of this package). The Playwright suite in `../tests` is unchanged and still the regression suite.

```bash
cd tests/e2e && npm ci                       # installs puppeteer-core alongside Playwright
npm run puppeteer:smoke                      # opens http://localhost:5090 (API :5080 + web must be running, demo seeded)
BASE_URL=none npm run puppeteer:smoke        # no app needed: proves Chrome launches and screenshots work
```

* `browser.mjs` — launch helper (`PUPPETEER_EXECUTABLE_PATH` overrides the Chrome path, `HEADED=1` shows the window),
  viewports 375/768/1440, `signIn(page, users.student)`, console-error and failed-request collectors, `screenshot()`.
* `smoke.mjs` — the smoke script above.
* `ui-audit.mjs [prefix] [roles] [viewports]` — full-page screenshots of every main page per role at 375/768/1440
  into `screenshots/<prefix>/`, with overflow, < 40 px touch targets, console errors and failed requests per page.
* `edge-check.mjs [width]` — elements past the right gutter at phone width (`html { overflow-x: hidden }` hides
  these from a plain `scrollWidth` check).
* `ui-refresh-detail.mjs` — viewport-sized captures of dashboards, stacked tables and the bottom-sheet dialog.
* `screenshots/` — git-ignored output; copy reviewed images into `docs/screenshots/` if they should be kept.
* Credentials: `E2E_PASSWORD` or `DEMO_PASSWORD` in the git-ignored repo `.env`; demo users are fictional
  (`*@horizon.example.test`). Never put real student data or secrets in scripts or screenshots.

Write new checks as `puppeteer/<flow>.mjs` importing from `browser.mjs`; run with `node puppeteer/<flow>.mjs`.
