> ## ⏸ PARKED — not part of the current phase
>
> **Owner decision 2026-11-04:** this phase delivers only the Student Sync **web portals and
> installable PWA** (`src/StudentSync.Web`). The Flutter app is kept here unchanged for a later
> phase but is **not built, tested, released or kept in parity** with the web app:
>
> - its GitHub Actions job is commented out in `.github/workflows/ci.yml`;
> - root docs, `CLAUDE.md` and the `.claude/skills` no longer ask for Flutter work;
> - API changes made during this phase are **not** mirrored here, so expect compile or
>   contract drift when the app is resumed (see `IMPLEMENTATION_STATUS.md`).
>
> To resume: uncomment the CI job, remove this banner, re-sync `lib/core/theme/tokens.dart`
> and the API models with the web app, then run `flutter analyze && flutter test`.

# Student Sync — mobile app (Flutter)

Native Android and iOS client for Student Sync. It talks only to the ASP.NET Core API
(`/api/v1`) — there is no business logic or fake data in the app; every screen is
backed by a real endpoint. Student Sync serves one institute: admin accounts are
directed to the web portal, parent accounts are refused by the server, and the mobile
app serves **students and teachers**. New students can apply from the sign-in screen.

- Flutter 3.41.6 / Dart 3.11.4 (stable)
- Brand tokens and typography follow the root `DESIGN.md` (`lib/core/theme/`)

## Setup

```bash
cd mobile/student_sync
flutter pub get
```

The API base URL is a build-time setting. Never hard-code it:

```bash
flutter run --dart-define=API_BASE_URL=https://api.your-domain.example/api/v1
```

Without `API_BASE_URL`, debug builds use a local development default
(`lib/core/config/app_config.dart`):

| Target | Default |
|---|---|
| iOS simulator | `http://localhost:5080/api/v1` |
| Android emulator | `http://10.0.2.2:5080/api/v1` (the emulator's alias for the host) |

Release builds must always pass `API_BASE_URL` (HTTPS). Plain HTTP is only allowed for
local development: Android allows cleartext only in the **debug** manifest, and iOS
only permits local networking (`NSAllowsLocalNetworking`).

### Run on the iOS simulator

```bash
open -a Simulator                       # or: xcrun simctl boot <udid>
flutter devices                         # find the simulator id
flutter run -d <simulator-id>           # uses http://localhost:5080/api/v1
```

### Run on an Android emulator

```bash
flutter emulators --launch <emulator-id>
flutter run -d emulator-5554            # uses http://10.0.2.2:5080/api/v1
```

### Demo accounts

Use the fictional demo accounts seeded by the backend (e.g. `aisha@horizon.example.test`,
`amira@horizon.example.test`). The demo
password is the development-only `DEMO_PASSWORD` value in the repository's `.env`; it
is intentionally not written anywhere in this app or its tests.

## Tests and checks

```bash
flutter analyze          # static analysis (strict casts / raw types enabled)
flutter test             # unit + widget tests (no network, no device)
```

Read-only on-device walkthrough against a running local API (signs in as student,
teacher and admin, checks that a parent is refused, navigates, never writes data) with
simulator screenshots. The video shots expect two fictional "(Test)" pre-recorded videos
on Amira's G10 Maths classes — one past and unlocked ("Factorisation recap (Test)"), one
upcoming and locked ("Completing the square — preview (Test)") — uploaded from the web app:

```bash
DEMO_PASSWORD=<from .env> scripts/capture_screenshots.sh <simulator-udid>
# screenshots land in docs/screenshots/
```

Build checks:

```bash
flutter build ios --simulator --debug
flutter build apk --debug
```

## Architecture

```
lib/
  main.dart, app.dart          bootstrap, lifecycle (unread refresh on resume)
  core/
    config/                    AppConfig (API_BASE_URL via --dart-define)
    api/                       ApiClient (single typed HTTP client), ApiException (RFC 7807)
    auth/                      AuthController (sign-in, serialised refresh, sign-out),
                               AuthApi (/auth, /me/password), TokenStore (secure storage)
    models/                    hand-written models mirroring src/StudentSync.Contracts
    routing/                   go_router config + pure redirect rules (auth/role guards)
    theme/                     tokens.dart (DESIGN.md), app_theme.dart, status_styles.dart
    time/                      TimeFormatter (IANA zones via package:timezone)
    push/                      PushTokenProvider + DeviceRegistrationService (POST /devices)
    widgets/                   SsCard, StatusBadge, SessionCard, StatCard, MetricTile,
                               AsyncView (loading/empty/error/forbidden), MarkdownView …
  features/
    auth/ shell/ student/ sessions/ videos/ learning/ doubts/ teacher/ parent/ profile/ notifications/
```

Key decisions:

- **Authentication.** Access tokens (15 min) and rotating refresh tokens are kept only
  in `flutter_secure_storage` (Keychain / Android Keystore; Android backups disabled).
  `AuthController` serialises refreshes: concurrent requests share one in-flight
  `POST /auth/refresh`, so a rotated refresh token is never presented twice (which would
  revoke the session family). `ApiClient` renews proactively 30 s before expiry and
  retries once after a 401. A rejected refresh signs the user out and the login screen
  says the session expired; a network failure during refresh keeps the session and shows
  a retryable error. `institution_code_required` reveals the institution-code field;
  `password_change_required` routes to the change-password screen (after which the app
  signs in again, because the server revokes all refresh tokens). `pending_approval`
  (self-registered student not yet approved) and `parent_access_disabled` show the
  server's message on the sign-in screen.
- **Registration.** `/register` (reachable only while signed out) loads
  `GET /auth/registration` and posts `POST /auth/register`. The server answers 202 the
  same way for a new or an existing email, so the form never reveals who has an account.
  The device's IANA zone is not sent (no time-zone plugin); the server uses the
  institute's zone and the student can change it in Profile.
- **Calendar.** The timetable (students reach it from Subjects and Home) and Classes have a Calendar / Upcoming /
  Past switch. The month grid is Monday-first like the web, days are the viewer's IANA
  zone (month bounds via `startOfViewerDayUtc`, DST-aware), each class is a dot and
  tapping a day lists its classes.
- **Pre-recorded videos.** Class page → Class videos section, Learning → Class videos (students) and the
  Classes app-bar button (teachers). The server decides the lock state and only sends a
  `fileId` when the viewer may play; the player fetches a fresh link each time, which the
  server refuses once a video is locked again. The class teacher sees Preview and an
  Access menu (Automatic / Unlocked now / Locked → `POST /videos/{id}/access`).
- **Player page.** One card holds the video and its controls (tap to play/pause, scrub,
  ±10 s, speed 0.75–2×). Below it: title, class and description; the class's questions
  (`GET /doubts?sessionId=`) with an Ask button for students; and the class's other
  videos. From 840 px wide (tablets, landscape) the details sit beside the player.
- **Roles.** go_router redirects keep each role inside its own shell
  with at most four tabs in a floating bottom bar (student: Home, Subjects, Learning, Doubts ·
  teacher: Home, Classes, Students, Tasks). Subjects lists the student's subjects; each opens its syllabus (`GET /my/subjects/{id}`): chapters in
  order, each with its live classes and videos. Learning keeps Assignments / Exams / Videos as tabs;
  class details, Doubts and Tasks show their content as sections on one scrolling page. Profile opens
  from the avatar at the top right of every tab. Phones use bottom navigation (platform convention);
  the grouped side menu is the web layout. The parent shell is still in the code but
  unreachable, because the server refuses parent sign-in.
  Hiding UI is never the security boundary — the API enforces authorisation and tenancy;
  the app renders a permission state on 403.
- **Every data region** renders exactly one of loading, empty, error-with-retry,
  forbidden, or content (`AsyncView`). Pull-to-refresh keeps content visible and reports
  failures in a snackbar.
- **Times** are stored/transferred in UTC and shown in the user's IANA zone
  (`user.timeZoneId`). When a session's organiser zone differs, the organiser time is
  shown too. DST is handled by the full tz database. Users can change their zone in
  Profile (`PUT /me/profile`).
- **Teaching modes** are labelled Batch class / Group session / One-to-one / Private group;
  sessions without a batch (one-to-one, private group) work everywhere.
- **Status colours** come from one mapping (`status_styles.dart`), mirroring the web.
- **Writes** that the server makes idempotent (`POST /doubts`, `PUT …/my-submission`)
  send an `Idempotency-Key`; concurrency-checked writes send the `rowVersion`.
- **Exams** autosave each answer (`PUT /attempts/{id}/answers`) — choices immediately,
  typing after a short pause — show a countdown to `deadlineUtc`, flush outstanding
  answers before submitting, and auto-submit at the deadline.
- **AI content** is never published from the app: teacher notes from AI drafts show as
  "AI-assisted · reviewed by your teacher"; pending AI drafts are listed for teachers
  with a pointer to the web review flow.

### Branding and motion

- **Logo** files come from `docs/brand/` (see its README for where the mark appears alone and
  where with the wordmark). iOS `AppIcon.appiconset` and Android `mipmap-*/ic_launcher.png` use the
  opaque white tile; the Android adaptive icon uses PNG foreground and monochrome layers. Icons
  were resized with ImageMagick; no icon package is used.
- **Launch screens** are soft white (`#F6F7FE`) with the 112-pt mark (iOS `LaunchScreen.storyboard`,
  Android `launch_background.xml`, Android 12+ `values-v31`). The Flutter splash matches them with
  the star turning and the wordmark below, then the mark glides (Hero) into the sign-in screen.
- **In app**: `BrandMark`, `BrandWordmark`, `BrandLockup`, `BrandAppBarTitle` and `BrandSpinner`
  (`lib/core/widgets/brand.dart`) show the bundled PNGs from `assets/brand/`; sign-in and
  registration share `AuthShell` (brand hero + form card on phones, brand panel + form from 840 px).
- **Visual language** (DESIGN.md §9): Lucide icons, real photos (profile photos from the API,
  bundled class covers and welcome photos), photo cards and carousels, a next-class photo card on each home, filled inputs, pill tabs and segmented controls, skeleton
  loading. Shared pieces live in `lib/core/widgets/` (`common.dart`, `session_card.dart`,
  `states.dart`).
- **Motion**: screens that replace each other (start-up, sign-in, home after sign-in) fade with a
  slight zoom; pushed screens use the platform transition; switching bottom tabs fades the new tab
  in while every tab keeps its state (`lib/core/routing/transitions.dart`).

### Packages

| Package | Why |
|---|---|
| `http` (dart.dev) | Minimal HTTP; our own client adds auth + serialised refresh, easy to test with `MockClient` |
| `flutter_secure_storage` | Keychain / Keystore-backed token storage |
| `go_router` (flutter.dev) | Declarative routes, auth redirects, deep-link-ready paths |
| `url_launcher` (flutter.dev) | Open meeting links, external materials, expiring file links |
| `video_player` (flutter.dev) | In-app recording playback. Pinned to `^2.11.1`: 2.12+ requires Flutter ≥ 3.44, this project is on 3.41.6 |
| `timezone` (labs.dart.dev) | IANA zones + DST for user/organiser times |
| `intl` (dart.dev) | Date formatting |
| `lucide_icons_flutter` | Lucide icon font (MIT, 160/160 pub points, actively released). Plain `IconData`, so it works with every `Icon` widget. Chosen over `phosphor_flutter` (no release since 2024) and `hugeicons` (custom widget, not `IconData`) |
| `image_picker` (flutter.dev) | Camera / photo library for the optional profile photo (resized to 1024 px, re-encoded as JPEG on the device) |
| `http_parser` (dart.dev) | Content type for the multipart photo upload (already a dependency of `http`) |
| `flutter_svg` (flutter.dev) | unDraw illustrations for empty, error and 404 states |
| `flutter_markdown_plus` | Markdown for notes/instructions — maintained successor of the discontinued `flutter_markdown` (pub.dev lists it as the replacement) |

Fonts (Inter, Plus Jakarta Sans) are bundled under `assets/fonts` (SIL OFL 1.1, licences
alongside) so the app never fetches fonts at runtime.

## Not implemented / limitations

- **Pre-recorded video uploads and editing** stay in the web app (the app has no file
  picker); teachers can preview and lock/unlock in the app. Admins use the web app for
  everything, including registration approval. Video watch progress is not tracked.

- **Push notifications are not delivered.** No FCM/APNs credentials exist. The device
  registration path (`PushTokenProvider` → `POST /devices`, unregister on sign-out) is in
  place behind an interface, but the shipped provider reports "not configured" and the
  app says so in Profile. Notifications are shown in-app from `GET /notifications`; the
  unread badge (`GET /notifications/unread`) refreshes on app resume, sign-in and
  pull-to-refresh only.
- **Recording playback** is in-app (`video_player`) for recordings that are `Available`
  and have a `fileId`; Play is hidden otherwise. The stream URL is a short-lived
  `GET /files/{fileId}/link` URL: if it expires mid-playback the player shows an error
  and Retry fetches a fresh link and resumes from the current position. Students resume
  from their saved position and their position is sent (`POST /recordings/{id}/progress`)
  on pause, on finish and when they leave the player mid-play; a failed save shows a
  message once and does not stop playback. "Open in browser" (shown when playback fails)
  adds a `#t=<seconds>` resume hint for students. **Not yet verified on a device or
  emulator:** the native iOS/Android player has only been statically analysed and
  covered by unit tests; no `flutter build` has been run since adding it.
- **Exam results** can be reopened from the exam list ("View result") using
  `latestAttemptId` when no attempt is in progress; this has not yet been exercised
  against the live API.
- **Offline caching** of notes/materials (`availableOffline`) is not implemented; the app
  needs a connection and shows an offline error state with Retry.
- **File uploads** (assignment attachments, doubt attachments, camera/gallery, voice
  questions) are not implemented; text answers and text doubts are. Existing attachments
  and private material files open through short-lived `GET /files/{id}/link` URLs.
- **Teacher authoring** (creating sessions, notes, assignments, exams; AI draft review /
  approval / publishing; agenda edits) stays in the web app. Exam grading of manual
  answers is web-only; assignment grading is available in the app.
- **Messaging and announcements** are not in the mobile app yet.
- **Deep links from outside the app** (universal links / app links) are not configured;
  in-app notification links route to the matching screen.
- **Localisation/RTL**: English only in v1; Arabic layout has not been verified. Dark
  mode is not shipped (as in DESIGN.md).
- The exam countdown uses the device clock against the server's `deadlineUtc`; the
  server remains the authority and rejects late answers.
- Release signing and store metadata are not set up. (App icons and launch screens are branded — see Branding.)
