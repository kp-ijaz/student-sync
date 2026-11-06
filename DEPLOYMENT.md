# Deployment guide

Student Sync ships as two stateless ASP.NET Core containers (API, Web) plus SQL Server 2022 and private file storage. Nothing here has been deployed publicly; this is the preparation for doing so.

## Images

```bash
docker build -f src/StudentSync.Api/Dockerfile -t studentsync-api .
docker build -f src/StudentSync.Web/Dockerfile -t studentsync-web .
```

Both listen on port 8080 as a non-root user. Put them behind a TLS-terminating reverse proxy or load balancer (HTTPS only; the web cookie is `Secure` outside Development). The web app needs sticky sessions or Azure SignalR/Redis backplane when scaled out (Blazor Server circuits).

## Configuration (environment variables)

| Setting | API | Web | Notes |
|---|---|---|---|
| `ConnectionStrings__Default` | ✓ | | SQL Server 2022; use a least-privilege login, encrypted connection |
| `Jwt__SigningKey` | ✓ | | ≥32 random chars from a secret manager; rotating it signs everyone out |
| `Jwt__AccessTokenMinutes`, `Jwt__RefreshTokenDays` | ✓ | | defaults 15 / 30 |
| `Database__MigrateOnStartup` | ✓ | | `false` in production — run `dotnet StudentSync.Api.dll migrate` as a release step |
| `Storage__Local__LocalRootPath` | ✓ | | Use a mounted volume; for multiple nodes implement `IFileStorage` on S3/Azure Blob |
| `DataProtection__KeysPath` | ✓ | ✓ | Shared, persistent location (protects file links and auth cookies) |
| `Email__Smtp__Host`, `Port`, `UseStartTls`, `Username`, `Password`, `FromAddress` | ✓ | | Without a host, email is recorded as "not configured" |
| `Ai__Anthropic__ApiKey`, `Ai__Anthropic__Model` | ✓ | | Optional, paid. Model defaults to `claude-opus-5-5` |
| `Cors__Origins__0…` | ✓ | | Only needed for browser clients calling the API directly |
| `Api__BaseUrl` | | ✓ | Internal URL of the API |
| `Api__PublicBaseUrl` | | ✓ | Public URL of the API (used for short-lived file/recording links) |
| `ASPNETCORE_ENVIRONMENT` | ✓ | ✓ | `Production` (demo seeding and the Hangfire dashboard are disabled outside Development) |

## Release procedure

1. Back up the database.
2. `docker run … studentsync-api migrate` (applies EF migrations and reference data; idempotent).
3. Deploy API, then Web. Check `GET /health/ready` (database) and `/health/live`.
4. Smoke test sign-in and a dashboard per role.

## Operations

* **Logs:** structured Serilog to stdout (no passwords, tokens or note/transcript content). Ship to your log platform.
* **Health:** `/health/live` (process) and `/health/ready` (database) on the API; `/health/live` on Web.
* **Background jobs:** Hangfire tables live in the `hangfire` schema of the same database. Recurring jobs are registered at API start-up.
* **Error monitoring:** hook an OpenTelemetry/Sentry/Application Insights exporter into Serilog (not configured by default).
* **Backups:** SQL Server full + log backups (encrypted), plus the file storage volume and Data Protection keys. Restore drills should restore all three together. Backup/restore has **not** yet been exercised in a documented environment.

## Production readiness checklist

- [ ] Secrets in a secret manager; `.env` never used in production
- [ ] HTTPS enforced at the edge; HSTS on
- [ ] `IFileStorage` on durable object storage with encryption at rest; malware scanning on upload
- [ ] Data Protection keys persisted and backed up
- [ ] SMTP provider with SPF/DKIM for the sending domain
- [ ] Decide on push provider (FCM/APNs) and add credentials
- [ ] Review recording/AI consent wording and retention with your privacy counsel for each jurisdiction
- [ ] Load test the web tier (Blazor Server circuits) and size SignalR
- [ ] Run the full test suites against the target environment
- [ ] Backup and restore drill completed
