---
name: frontend-performance
description: Review and improve client-side performance of Student Sync screens — Blazor Server render/re-render cost, payload sizes, image/video loading, large lists, and Flutter frame cost. Use for any performance complaint, any list/table/calendar/player that can grow large, and before completing a data-heavy screen (dashboards, student 360, timetables, question banks, document chapters).
---

# Frontend performance (Student Sync)

> **Flutter is parked this phase (owner decision 2026-11-04).** Student Sync currently ships
> the web portals and the installable PWA only. Treat every Flutter/mobile-app instruction in
> this skill as suspended: do not build, test, mirror or verify anything under
> `mobile/student_sync` until the owner re-enables the app.

## Purpose and triggers

The web app is **Blazor Server (interactive server render mode)**: every re-render is diffed on the
server and sent over the SignalR circuit, so unnecessary `StateHasChanged`, large component trees and
chatty API calls show up as latency and server CPU. Use this skill when: a page feels slow, a list can
exceed ~100 rows (students, sessions, questions, ledger entries, extracted chapters), media is involved
(covers, photos, videos, PDFs), a dashboard aggregates several API calls, or a Flutter screen drops frames.
Pair with `api-integration` (pagination/bounded queries) and `puppeteer-browser-testing` (measurements).

## Inspect first (real files)

- Render mode and static assets: `src/StudentSync.Web/Program.cs` (`AddInteractiveServerComponents`,
  `MapStaticAssets()` → fingerprinted, compressed static files) and `Components/App.razor` (`<ImportMap />`,
  Google Fonts preconnect, KaTeX auto-render from cdnjs loaded `defer`).
- Reconnection UX: `Components/Layout/ReconnectModal.razor` (circuit drop handling).
- Data loading pattern: `Components/Shared/DataView.razor` (one loading/error/empty wrapper per region) and
  `Services/ApiClient.cs` (typed calls; check for sequential awaits that could be `Task.WhenAll`).
- Keyed child components: `Components/Pages/Learning/SessionHub.razor` uses `@key` for the class view —
  the pattern for lists that re-order.
- Media: `Components/Shared/ClassCover.razor`, `Avatar.razor`, `Student/MediaPlayer.razor`, `VideoList.razor`;
  images in `wwwroot/images/covers`, `wwwroot/images/welcome`; private files served through short-lived
  links from the API (`/files` endpoints in `src/StudentSync.Api/Endpoints/LearningEndpoints.cs`).
- Offline/PWA: `wwwroot/service-worker.js` (caches static assets only), `wwwroot/js/app.js`.
- Paging: `Components/Shared/Pager.razor`; API list endpoints return bounded pages — never fetch "all".
- Flutter: `lib/core/widgets/async_view.dart`, `session_card.dart`, `class_cover.dart` (asset covers), list
  screens under `lib/features/*` (use `ListView.builder`/slivers; `const` widgets; avoid rebuilding whole
  screens on timers such as the exam countdown).
- CI/format gates: `dotnet format` (no perf tooling is configured; Lighthouse is optional per the spec).

## Rules that must not be broken

- Lists that can grow are **paged server-side** (`Pager` + API page size); on the client use `@key` on
  row components and `Virtualize` for long static lists. No N+1 API calls inside loops.
- One `DataView` per region, loaded in parallel (`Task.WhenAll`) when regions are independent; show
  partial content rather than blocking a dashboard on its slowest card.
- Timers (exam countdown, "join from 15 min before", live badges) update a small child component, not
  the page; stop them in `Dispose`.
- Images: fixed `width/height` or aspect boxes (no layout shift), `loading="lazy"` for below-the-fold
  covers/photos, sizes appropriate to the slot (avatars never load full-size photos).
- Video/PDF/document content streams from the API with range support; never inline base64, never load a
  whole PDF to render a chapter list (use the extracted chapter metadata).
- Do not add client libraries for one screen; KaTeX is the only third-party script, loaded deferred.
  Any new script must be from the allowed CDNs with a pinned version and justified in the report.
- Static assets go through `MapStaticAssets` (fingerprint + compression); do not bypass with inline styles
  or data URIs larger than a few KB.
- Flutter: build methods cheap (`const`, split widgets), images cached/sized, no synchronous file work on
  the UI thread, `flutter analyze` clean.

## Workflow

1. Define the scenario and a budget: page with realistic demo data (seed via `seed-demo`), target
   interaction (open page, change filter, scroll 200 rows, start exam), acceptable time (e.g. < 1 s to
   content on a local stack, no visible jank).
2. Measure before changing: Puppeteer `page.metrics()`, Navigation/Resource timing
   (`performance.getEntriesByType("resource")`) for payload counts and sizes, number of API requests
   (`read_network_requests`/request listener), DOM node count (`document.querySelectorAll("*").length`),
   and the SignalR message volume for an interaction if re-rendering is suspected (dev tools / server logs).
3. Find the cause: sequential awaits, unbounded queries, re-render storms (`StateHasChanged` in loops or
   timers), heavy components without `@key`, unsized media, repeated expensive computations in `Render`.
4. Fix at the narrowest layer: API page size/projection (`api-integration`), parallel loads, child
   component with its own state, `Virtualize`, lazy media, memoised view models.
5. Re-measure with the same script; record before/after numbers in the report.
6. Confirm no behaviour change: the page's Playwright/Puppeteer journey and axe still pass.

## Security / permission notes

Performance work must not cache or prefetch data across roles/users (the web session store is in-memory
per node; never put per-student data in the service worker cache beyond the explicitly saved offline
notes). Signed file links stay short-lived even when "preloading" media.

## Required checks

- Before/after measurements for the scenario (requests, bytes, DOM nodes, time-to-content) captured with
  Puppeteer on the local stack, at 1440 px and 375 px.
- No console errors; no failed/duplicated requests; API calls per page load counted.
- The page's existing e2e journey, overflow and axe checks still pass.
- Flutter: `flutter analyze` and a profile-mode run note if frames were the issue.

## Acceptance criteria

- The scenario meets the stated budget, or the remaining gap and its cause are documented.
- No unbounded list, no N+1 request pattern, no layout shift from media, timers scoped to small components.
- No new third-party dependency without justification.

## Completion checklist (report these)

- [ ] Scenario, budget and before/after numbers.
- [ ] Root cause and the layer fixed (API / page / component / asset).
- [ ] Regression checks run (journey, overflow, axe) and results.
- [ ] Anything not measured or not verified.
