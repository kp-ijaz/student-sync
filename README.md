# Student Sync

**LEARN • CONNECT • GROW** — a multi-tenant tuition and learning platform for tuition centres, teachers, students and parents.

Student Sync supports **batch classes, ad hoc group sessions, private groups and one-to-one tuition** (a session never needs a batch), with scheduling across time zones, a session hub (notes, materials, recordings, attendance), assignments, exams, a Doubt Centre with automatic follow-up, messaging and notifications, an AI Class Assistant with mandatory teacher approval, role dashboards, reports and SaaS administration.

| Part | Technology | Location |
|---|---|---|
| API | ASP.NET Core (.NET 10), EF Core 10, SQL Server 2022, Identity + JWT, SignalR, Hangfire | `src/StudentSync.Api`, `src/StudentSync.Application`, `src/StudentSync.Infrastructure`, `src/StudentSync.Domain` |
| Web + PWA | Blazor Web App (Interactive Server, backend-for-frontend) | `src/StudentSync.Web` |
| Mobile | Flutter (Android & iOS) — **parked this phase**, not built or tested; web + PWA only | `mobile/student_sync` |
| Tests | xUnit v3 (unit + SQL Server integration via Testcontainers), Playwright + axe (E2E, accessibility, responsive) | `tests/` |

## Quick start (local development)

Requirements: .NET SDK 10, Docker, Node 20+ (for E2E tests). See [DEVELOPMENT.md](DEVELOPMENT.md) for details.

```bash
cp .env.example .env            # then fill in the values (see comments)
docker compose up -d sqlserver mailpit
dotnet run --project src/StudentSync.Api -- seed-demo   # migrate + fictional demo data
dotnet run --project src/StudentSync.Api                 # http://localhost:5080  (Swagger: /swagger)
dotnet run --project src/StudentSync.Web                 # http://localhost:5090
```

Or run everything in containers: `docker compose --profile app up -d --build`, then `docker compose --profile app run --rm api seed-demo`.

## Documentation

* [ARCHITECTURE.md](ARCHITECTURE.md) — system design, ERD, tenancy, security, data strategy, AI pipeline, decisions
* [DESIGN.md](DESIGN.md) — design system (tokens, typography, components, accessibility)
* [DEVELOPMENT.md](DEVELOPMENT.md) — setup, demo accounts, commands, testing
* [DEPLOYMENT.md](DEPLOYMENT.md) — configuration, production checklist, backups
* [IMPLEMENTATION_STATUS.md](IMPLEMENTATION_STATUS.md) — what is built, verified, limited or not done
* [docs/USER_GUIDE.md](docs/USER_GUIDE.md) — guides for administrators, teachers, students and parents
* [mobile/student_sync/README.md](mobile/student_sync/README.md) — Flutter app (parked since 2026-11-04; kept for a later phase)

## External services

Nothing paid is enabled by default. Email uses any SMTP server (local Mailpit in development). The AI Class Assistant needs an Anthropic API key (`ANTHROPIC_API_KEY` / `Ai__Anthropic__ApiKey`); without it the app says so plainly and manual notes keep working. Push notifications, automatic transcription, Google Meet/Calendar OAuth and payment collection are not configured — see IMPLEMENTATION_STATUS.md.

## Product specification

* `CLAUDE.md` — engineering rules and the authoritative institute-workflow addendum (sections A–O).
* `Student_Sync_Institute_Workflow_Master_Prompt.md` — detailed institute workflow specification (2026-11-04).
* `Student_Sync_Master_Prompt.md` — original platform specification; superseded where the addendum conflicts.
* `docs/INSTITUTE_WORKFLOW_GAP_ANALYSIS.md` — audit, gaps and implementation plan.
