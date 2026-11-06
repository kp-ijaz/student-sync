---
name: ui-ux-design-system
description: Maintain and apply the Student Sync design system — tokens, typography, spacing, radii, elevation, icons, status colours, shared components and navigation patterns — on web (Blazor CSS) and Flutter in parity. Use whenever a token, shared component, status mapping or menu structure is added or changed, or when checking a screen for design consistency.
---

# UI/UX design system (Student Sync)

> **Flutter is parked this phase (owner decision 2026-11-04).** Student Sync currently ships
> the web portals and the installable PWA only. Treat every Flutter/mobile-app instruction in
> this skill as suspended: do not build, test, mirror or verify anything under
> `mobile/student_sync` until the owner re-enables the app.

## Purpose and triggers

Keeps one visual language across `src/StudentSync.Web` and `mobile/student_sync`. Use it when:
- adding/changing a CSS token, class in `app.css`, or a Dart token;
- adding/changing a shared component (`Components/Shared/*.razor`, `lib/core/widgets/*.dart`);
- adding a status, teaching mode, role label or icon mapping;
- changing navigation groups in `Navigation.For`;
- reviewing a screen for consistency ("does this match the rest of the app?").
Pair with `frontend-design` for page composition and `visual-regression-qa` for styling changes.

## Inspect first (real files)

- `DESIGN.md` — §1 tokens, §2 typography, §3 spacing/radius/elevation, §4 layout & navigation, §5 component
  table (web ↔ Flutter equivalents and parameters), §6 accessibility, §7 motion, §8 voice, §9 Flutter.
- Web tokens: `src/StudentSync.Web/wwwroot/css/app.css` lines 1–21 (`:root`: `--ss-*` colours, `--sp-1…8`,
  `--r-card/--r-btn/--r-control/--r-sheet/--r-pill`, `--shadow-1/2/hero`, `--grad-brand`, `--ease`,
  `--font-head/--font-body`, `--sidebar: 248px`). Fonts are loaded in `Components/App.razor` (Google Fonts:
  Inter 400–700, Plus Jakarta Sans 600/700).
- Flutter tokens: `mobile/student_sync/lib/core/theme/tokens.dart` (`SsColors`, `SsSpace`, `SsRadius`,
  `SsShadow`, gradients), theme wiring in `app_theme.dart`, status mapping in `status_styles.dart`.
- Single sources of mappings: `src/StudentSync.Web/Services/UiServices.cs` (`StatusStyles`, `Navigation`,
  `Illustrations`, `ClassCovers`, `I18n`) and its Dart twins (`status_styles.dart`, `class_cover.dart`).
- Component gallery: `src/StudentSync.Web/Components/Pages/DevUiKit.razor` → `/dev/ui` (Development only).
- Brand assets and usage rules: `docs/brand/README.md`; photo/illustration credits in
  `wwwroot/images/CREDITS.md` and `wwwroot/illustrations/CREDITS.md`.
- Keyboard behaviour for tabs/segmented controls: `src/StudentSync.Web/wwwroot/js/app.js`.

## Rules that must not be broken

- **Tokens only.** Colours, spacing (4/8/12/16/24/32/48/64), radii (20 card, 14 control, 28 sheet, 999 pill),
  shadows and fonts come from `:root` / `tokens.dart`. A new value needs a new token in **both** platforms
  and a row in DESIGN.md §1/§3 in the same change.
- **Contrast-safe text:** body `#1E293B`, secondary `#475569`; Slate `#64748B` only for icons/borders/large
  text; orange never as body text (use `--ss-orange-text`/`--ss-orange-deep` on light surfaces).
- **Gradient placement is fixed** (sign-in hero, `HeroCard`, selected pill of `Segmented`/`PillTabs`/
  `.segmented`/`.tabs`, initials avatar). Nowhere else.
- **Status colours have one source** (`StatusStyles` / `status_styles.dart`). Add new statuses (e.g. hour
  ledger, package, diagnostic, class-report, document-processing states from CLAUDE.md addendum K) there,
  with text + colour (never colour alone), and mirror them in Dart.
- **Navigation is a grouped side menu** (`Navigation.For`), docked at ≥ 1024 px, drawer below; one link
  marked current. No top navigation (DESIGN.md §4). Below 1024 px the shell adds the mobile app's floating
  four-tab bar (`Navigation.Tabs`, `.tabbar` in `app.css`) and the app bar shows the page title via `ShellState`.
- **Shared first.** Compose from `Components/Shared`; extend with parameters/slots rather than cloning.
  Avoid one component with dozens of unrelated flags. New shared components get a `/dev/ui` sample and a
  DESIGN.md §5 row with their Flutter equivalent (or "—").
- **Controls:** filled inputs (`--ss-fill`), 1.6 px blue focus border, 48 px buttons (`.btn-sm` 36 px),
  2 px visible focus outline everywhere, touch targets ≥ 44 px on mobile.
- **Motion:** 150–200 ms ease-out, `prefers-reduced-motion` respected (global rule in `app.css` line 402).
- Dark mode is not shipped; keep tokens as custom properties so it can be added later.

## Workflow

1. Read the DESIGN.md section that governs the change and the current token/component implementation.
2. Decide: reuse (preferred) → extend (new parameter/slot) → new token/component (justify in the PR note).
3. Make the change in web **and** Flutter when it is a token, status mapping or shared pattern; if one side
   is deliberately not changed, say so in the report.
4. Update `DESIGN.md` (and `/dev/ui` for web components) in the same change.
5. Grep for every usage of a changed class/component (`grep -rn "ClassName" src/StudentSync.Web/Components`)
   and check each affected page in the browser; a shared change is regression-tested across its users.
6. Run the checks below.

## Security / permission notes

Design-system components never decide access. `AuthorizeView`/menu filtering only hides; the API in
`src/StudentSync.Api/Endpoints` enforces permissions. Illustrated "access denied" and 404 states
(`StateView`, `Forbidden.razor`, `NotFound.razor`) must not leak whether a record exists for another tenant
or student (use the same message as a not-found).

## Required checks

- `/dev/ui` reviewed in the browser after any shared-component change (desktop + 375 px via
  `puppeteer-browser-testing`).
- Pages that use the changed component: screenshots at 375/768/1440 px; `visual-regression-qa` for the
  Playwright pixel baselines (`tests/e2e/tests/visual.spec.ts-snapshots`) if sign-in/offline were touched.
- axe on at least one page per role that renders the component (`accessibility-audit`).
- Flutter: `flutter analyze`, `flutter test` (widget tests under `mobile/student_sync/test/`).
- `dotnet format StudentSync.slnx --verify-no-changes --severity error` still clean.

## Acceptance criteria

- No hard-coded colour/size values outside the token files; both platforms agree on changed tokens.
- DESIGN.md and `/dev/ui` reflect the change; status mapping has a single source per platform.
- All pages using a changed shared component were viewed and show no regressions.
- Contrast and focus rules hold (axe clean); navigation remains a grouped side menu.

## Completion checklist (report these)

- [ ] Tokens/components changed, with the DESIGN.md rows updated.
- [ ] Web/Flutter parity status (changed both / deliberately one side, why).
- [ ] List of consuming pages checked, with screenshot paths.
- [ ] axe and format results; anything not verified.
