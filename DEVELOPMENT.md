# Development guide

## Prerequisites

* .NET SDK 10.0.1xx (`global.json` rolls forward to the latest feature band)
* Docker (SQL Server 2022 and Mailpit run in containers; on Apple Silicon SQL Server runs under amd64 emulation)
* `dotnet-ef` 10 (`dotnet tool install -g dotnet-ef`) for creating migrations
* Node.js 20+ for the Playwright tests. (Flutter is not needed: the mobile app is parked this phase.)

## 1. Configure

```bash
cp .env.example .env
```

Fill in `MSSQL_SA_PASSWORD`, `JWT_SIGNING_KEY` (`openssl rand -base64 48`) and `DEMO_PASSWORD`. `.env` is git-ignored. In the `Development` environment the API reads it automatically (it builds the connection string from `MSSQL_SA_PASSWORD`/`SQL_PORT`). If `JWT_SIGNING_KEY` is empty in Development, a random key is generated into `src/StudentSync.Api/App_Data/` (also git-ignored).

## 2. Start dependencies

```bash
docker compose up -d sqlserver mailpit
# On Apple-silicon Macs the amd64 SQL Server image runs under emulation and can crash (exit 139);
# docker-compose.yml restarts it automatically (restart: unless-stopped).
```

* SQL Server: `localhost,14350` (user `sa`, password from `.env`)
* Mailpit (captures all email, nothing is delivered externally): http://localhost:18025

## 3. Database and demo data

```bash
dotnet run --project src/StudentSync.Api -- migrate      # apply migrations + plans/feature flags
dotnet run --project src/StudentSync.Api -- seed-demo    # Development only; safe to run repeatedly
dotnet run --project src/StudentSync.Api -- reset-demo --confirm-development   # drops & recreates the dev DB
```

`seed-demo` creates two fictional institutions inside one transaction and does nothing if they already exist. The product now runs as a single institute (Horizon); the second institution is kept only for tenant-isolation tests.

**Student accounts are created by admins only** (*People ▸ Students ▸ Add student*): the dialog shows a one-time temporary password that the student must change at first sign-in. Admins can re-issue one with *Reset password* on the edit dialog. There is no public registration page.

## 4. Run

```bash
dotnet run --project src/StudentSync.Api   # http://localhost:5080 — Swagger UI /swagger, Hangfire /hangfire (localhost only)
dotnet run --project src/StudentSync.Web   # http://localhost:5090
```

## Demo accounts (development only)

All demo accounts are fictional, use the non-deliverable `example.test` domain and share the password in `DEMO_PASSWORD` from your `.env`. Set `DEMO_FORCE_PASSWORD_CHANGE=true` to make each account change its password at first sign-in.

| Role | Email | Notes |
|---|---|---|
| Platform super admin | `superadmin@studentsync.example.test` | Multi-institute administration; not needed for a single institute and slated for removal |
| Admin (Horizon) | `admin@horizon.example.test`, `admin2@horizon.example.test` | Institution "Horizon Tuition Academy (Demo)", code `horizon-demo`, Asia/Dubai |
| Teacher | `amira@horizon.example.test` | Batch "G10 Maths — Evening A" (Mon/Wed 19:00 Dubai) |
| Teacher | `priya@horizon.example.test` | Recurring one-to-one chemistry with Fatima (no batch), Asia/Kolkata |
| Teacher | `daniel@horizon.example.test` | Ad hoc physics group + private group |
| Teacher | `lucas@horizon.example.test` | One-to-one IELTS writing, Europe/London; Class 10 English (timetable 14:30) |
| Teacher | `meera@horizon.example.test` | Meera Nair — languages: assigned to the Class 10 Hindi/Malayalam/Arabic/French subjects; teaches the Hindi (Tue) and Malayalam (Thu) second-language groups at 17:30 Dubai; no photo |
| Student | `aisha@`, `ben@`, `chloe@`, `dev@`, `ethan@` `horizon.example.test` | Batch students (various time zones); Class 10 Section A timetable |
| Student | `fatima@horizon.example.test` | One-to-one only; has an unresolved doubt on the next lesson's agenda |
| Student | `gabriel@horizon.example.test`, `hiro@horizon.example.test` | One-to-one IELTS / not in any batch |
| Parent | `parent.khan@horizon.example.test`, `parent.almansoori@horizon.example.test` | Kept in the data but **cannot sign in**: the institute runs without a parent portal |
| Admin (Northbridge) | `admin@northbridge.example.test`, `teacher@northbridge.example.test`, `student@northbridge.example.test` | Second institution for isolation testing (`northbridge-demo`) |

**Demo profile photos.** `seed-demo` gives the eight demo staff accounts (Horizon teachers Amira, Priya, Daniel, Lucas; admins Hana and Omar; Northbridge's Oliver and Nora) a profile photo from `src/StudentSync.Infrastructure/Seeding/DemoPhotos/<key>.jpg` (embedded resources; licence and sources in `CREDITS.md` there). Demo students get no photo and show initials; they can upload their own on *Profile*. Photo seeding runs on every `seed-demo`, also when the demo data already exists, and only fills accounts that have no photo, so re-running changes nothing; a missing image is skipped with a warning.

**Demo syllabus.** Every `seed-demo` also makes sure the extra coaching subjects have a fictional syllabus (Physics 5 chapters, Chemistry 4, IELTS Writing 3), shown to students through `GET /api/v1/my/subjects/{id}`. Chapters are matched by title, so existing chapters keep their ids, only missing ones are added and sort orders are corrected; re-running changes nothing.

**Demo CBSE curriculum.** Horizon teaches CBSE / NCERT, Classes 1–12. Every `seed-demo` ensures (matched by code/title, idempotent):

* Grades "Class 1" … "Class 12" (the old "Grade 9/10/11" rows were renamed, ids kept) and one course per standard — `CBSE-C1` "Class 1 (CBSE)" … `CBSE-C10` "Class 10 (CBSE)" (the former "Grade 10 Science & Mathematics" course) — plus Science/Commerce/Humanities courses for 11 and 12 (`CBSE-C11-SCI` …).
* Core subjects (every student enrolled in the course takes them): 1–2 English, Mathematics; 3–5 + The World Around Us (EVS); 6–10 English, Mathematics, Science, Social Science; 11–12 Science: English Core, Physics, Chemistry, Mathematics, Biology; Commerce: English Core, Accountancy, Business Studies, Economics (electives Mathematics, Applied Mathematics, Informatics Practices are *Additional*); Humanities: English Core, History, Political Science, Geography, Economics (Psychology *Additional*).
* Languages are **configurable choice groups**, not rules in code: "Second language" (1–10), "Third language" (6–10) and "Language (optional)" (11–12), each with Hindi, Malayalam, Arabic and French (*LanguageOption* subjects). Codes look like `C10-L2-HIN`, `C11S-PHY`, `C6-SST`; Class 10 Mathematics keeps its old code `MATH10`.
* NCERT chapter titles for the Class 10 core subjects (Mathematics 14, Science 13, English — First Flight 9, Social Science 10). The earlier maths chapters keep their ids ("Trigonometry" became "Introduction to Trigonometry"; unreferenced fictional chapters were removed).
* Physics (`PHYS10`), Chemistry (`CHEM10`) and IELTS Writing (`IELTS-W`) are *Additional* (extra coaching) subjects.

Once (the first time the curriculum is created) it also: enrols all eight Horizon demo students in Class 10 (CBSE), gives each a second and third language (e.g. Aisha: Hindi + French; Ben: Malayalam + Hindi; Fatima: Arabic + Hindi; Gabriel: French + Hindi), assigns the extra coaching subjects to exactly the students who had classes in them, creates batch "Class 10 — Section A" (`C10-A`, the five batch students) and an hourly **Class 10 timetable** in Asia/Dubai from two weeks ago to four weeks ahead: Mon–Fri 14:30 English (Lucas), 15:30 Mathematics (Amira), 16:30 Science (Daniel), 17:30 Social Science (Priya, Mon/Wed/Fri) or a second-language group (Meera: Hindi on Tuesday, Malayalam on Thursday). Past classes are completed with attendance; two recent English and Science classes each have a published note and an unlocked pre-recorded video record (a placeholder file, no real media). Occurrences that would overlap an existing class of the same teacher or student are skipped. Admins change a student's languages and extras on the student's profile (*People ▸ Students ▸ student ▸ Subjects*) or with `PUT /api/v1/students/{id}/subjects`.

The demo AI drafts are labelled "Demo content — not generated by an AI model"; the demo recording is metadata only (no media).

## Testing

```bash
dotnet test --project tests/StudentSync.UnitTests          # domain & application rules (no database)
dotnet test --project tests/StudentSync.IntegrationTests   # real API + SQL Server 2022 (Testcontainers)
```

Integration tests start a disposable SQL Server container. To reuse the compose server instead, set `STUDENTSYNC_TEST_SQL="Server=localhost,14350;User Id=sa;Password=…"`; each run uses and then drops its own uniquely named database.

Browser tests (API + web running, demo data seeded):

```bash
cd tests/e2e && npm ci && npx playwright install chromium
npx playwright test            # desktop journeys, 375px responsive checks, axe WCAG 2.1 AA scans
npx playwright show-report
```

The password is read from `E2E_PASSWORD` or `.env` (`DEMO_PASSWORD`), never from the test files.

Formatting: `dotnet format StudentSync.slnx --verify-no-changes --severity error`.

## Creating a migration

```bash
dotnet ef migrations add <Name> --project src/StudentSync.Infrastructure --startup-project src/StudentSync.Api --output-dir Persistence/Migrations
```

Review the generated migration (keys, delete behaviour, data preservation) before applying it. Never edit or delete a migration that may have been applied elsewhere.

## Project conventions

* Business rules belong in Domain entities or Application services, never in Razor components or endpoints.
* Every service method that reads tenant data goes through `IAppDbContext` (tenant-filtered) and `AccessPolicy` (resource-level).
* Status labels and colours come from `StatusStyles` (web). (`status_styles.dart` in the parked Flutter app mirrors it; do not update it this phase.)
* Every data region uses `DataView` to render loading, error (with retry), forbidden, empty or content states.
