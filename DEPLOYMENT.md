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
| `Jobs__Mode` | ✓ | | `Hangfire` (default) or `Inline`: no Hangfire server or tables; enqueued jobs run in-process, delayed and recurring jobs do not run. Only for tests and the free demo environment below |
| `Storage__Local__LocalRootPath` | ✓ | | Use a mounted volume; for multiple nodes implement `IFileStorage` on S3/Azure Blob |
| `DataProtection__KeysPath` | ✓ | ✓ | Shared, persistent location (protects file links and auth cookies) |
| `Email__Smtp__Host`, `Port`, `UseStartTls`, `Username`, `Password`, `FromAddress` | ✓ | | Without a host, email is recorded as "not configured" |
| `Ai__Anthropic__ApiKey`, `Ai__Anthropic__Model` | ✓ | | Optional, paid. Model defaults to `claude-opus-5-5` |
| `Cors__Origins__0…` | ✓ | | Only needed for browser clients calling the API directly |
| `Api__BaseUrl` | | ✓ | Internal URL of the API |
| `Api__PublicBaseUrl` | | ✓ | Public URL of the API (used for short-lived file/recording links) |
| `Sessions__StorePath` | | ✓ | Directory for signed-in sessions (tokens per session, encrypted with the Data Protection keys); the web image sets `/app/sessions`. Mount a persistent volume so restarts and redeploys keep people signed in. Empty = in memory (everyone is signed out on restart). One web node only; several nodes need a shared cache (Redis / SQL Server) |
| `Sessions__PersistentDays` | | ✓ | How long *Keep me signed in* lasts, sliding (default 30). Keep it at or below `Jwt__RefreshTokenDays` |
| `ASPNETCORE_ENVIRONMENT` | ✓ | ✓ | `Production` (demo seeding and the Hangfire dashboard are disabled outside Development) |

## Release procedure

1. Back up the database.
2. `docker run … studentsync-api migrate` (applies EF migrations and reference data; idempotent).

**First release only: create the institute and its first administrator.** No account exists on a new database and there is no self-registration, so run this once (it migrates first, and is refused as soon as any institution exists):

```bash
docker run … studentsync-api create-institution --name "<institute name>" --code <sign-in code> \
  --time-zone <IANA id, e.g. Asia/Dubai> --country <ISO-2> --currency <ISO-3> --admin-email <email> [--admin-name "<full name>"]
```

It prints a generated temporary password once; the administrator must change it at first sign-in. To choose the temporary password yourself, set the environment variable `Bootstrap__AdminPassword` for that run (never a command-line argument). One that does not meet the password policy is refused unless `--allow-weak-temporary-password` is passed; it still has to be changed at first sign-in. Name, country, time zone and currency can be edited later under *Settings*.
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

## Free demo environment (Oracle Always Free + Azure SQL free offer)

A zero-cost, publicly reachable **demo/trial** environment, started either empty (`create-institution`) or with the fictional `seed-demo` data. It is not a production set-up (see the limits below) and should not hold real student data. Files are in [`deploy/oracle/`](deploy/oracle/).

| Piece | Where | Notes |
|---|---|---|
| Web (Blazor Server) + Caddy | Oracle `VM.Standard.E2.1.Micro` (x64, 1 GB) | Caddy terminates HTTPS for the app and API host names (automatic Let's Encrypt certificates) and proxies the API host to the API VM |
| API | second `VM.Standard.E2.1.Micro` | Port 8080 is open to the VCN only; uploads and Data Protection keys live in Docker volumes on the boot disk |
| Database | Azure SQL Database free offer (serverless, auto-pause, exhaustion behaviour *AutoPause*) | SQL Server cannot run on Always Free VMs (x64 only, needs 2 GB RAM). Firewall admits the API VM's public IP only; the app uses a database-scoped user, not the server admin |

**Why two micro VMs.** A tenancy's Always Free ARM allowance may already be used; the AMD micro shape is separate. One .NET app per 1 GB VM leaves headroom (measured idle: API about 165 MB, Web about 100 MB, Caddy about 30 MB; 2 GB swap is added at first boot).

**Limits you accept**

* `Jobs__Mode=Inline`: Hangfire's polling would keep the serverless database awake and use up the monthly free allowance (100,000 vCore seconds, roughly 55 hours awake) within days. Session/assignment reminders, doubt follow-up, the expired-exam sweep, recording retention, the weekly digest, quiet-hours deferral and email retries therefore do **not** run.
* When the monthly allowance is used up the database pauses until the next calendar month and the site cannot sign anyone in. The database also pauses after an hour without connections; the first request afterwards waits while it resumes (the connection string in `api.env.example` retries for this).
* Uptime checks must use `/health/live`, never `/health/ready` (it queries the database and keeps it awake).
* Web-to-API traffic crosses the private subnet unencrypted. Signed-in sessions live on the `websessions` volume, so restarts and redeploys keep people signed in; deleting that volume or the `webkeys` volume signs everyone out.
* No SMTP, no object storage, no backups of the VM volumes. Oracle may reclaim Always Free instances that stay idle for 7 days.

**First-time set-up (summary)**

1. Azure: logical server + database with `--use-free-limit true --free-limit-exhaustion-behavior AutoPause` (serverless General Purpose); firewall rule for the API VM's public IP; a contained user for the app (`CREATE USER … WITH PASSWORD`, `ALTER ROLE db_owner ADD MEMBER …`).
2. Oracle: a VCN with a public subnet; security list allowing 22, 80 and 443 from anywhere and 8080 from the VCN CIDR only; two Ubuntu 24.04 Minimal micro instances launched with `cloud-init.yaml` (swap, Docker, Compose).
3. Host names: any DNS names pointing at the web VM. Without a domain, `app.<ip-with-dashes>.sslip.io` and `api.<ip-with-dashes>.sslip.io` work.
4. On each VM create `/opt/studentsync/api.env` or `web.env` from the `*.example` files (`chmod 600`). Keep generated secrets outside the repository.
5. `deploy/oracle/deploy.sh all` publishes both apps for linux-x64 on your machine, copies the output, builds the runtime image on each VM and starts the services. It needs `~/.studentsync-deploy/ssh_config` with hosts `ss-api` and `ss-web`.
6. Either start empty with `docker compose -f compose.api.yml run --rm api create-institution …` (see *Release procedure*), or load demo data, once, on the API VM (the seeder only runs in the Development environment, so the one-off container overrides it; use a strong password and keep the forced change at first sign-in):

   ```bash
   printf 'ASPNETCORE_ENVIRONMENT=Development\nDemo__Password=<strong password>\nDemo__ForcePasswordChange=true\nEmail__Smtp__Host=\n' > seed.env
   docker compose -f compose.api.yml run --rm --env-from-file seed.env api seed-demo && rm seed.env
   ```

**Updating:** `deploy/oracle/deploy.sh [api|web|all]`. Migrations run when the API starts.

**Starting again from empty:** stop the API, drop every table in the Azure database (do not drop the database itself: that would lose the free-offer settings and the app user), delete the contents of the `storage` volume, run `create-institution`, start the API, then empty the web `websessions` volume and restart the web container (old sign-ins point at accounts that no longer exist).

**Before real use:** a paid or self-hosted database tier, Hangfire back on, SMTP, durable file storage and backups — see the checklist above.
