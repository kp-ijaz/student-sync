# Deployment guide

Student Sync ships as two stateless ASP.NET Core containers (API, Web) plus PostgreSQL 18 and private file storage. Nothing here has been deployed publicly; this is the preparation for doing so.

## Images

```bash
docker build -f src/StudentSync.Api/Dockerfile -t studentsync-api .
docker build -f src/StudentSync.Web/Dockerfile -t studentsync-web .
```

Both listen on port 8080 as a non-root user. Put them behind a TLS-terminating reverse proxy or load balancer (HTTPS only; the web cookie is `Secure` outside Development). The web app needs sticky sessions or Azure SignalR/Redis backplane when scaled out (Blazor Server circuits).

## Configuration (environment variables)

| Setting | API | Web | Notes |
|---|---|---|---|
| `ConnectionStrings__Default` | ✓ | | PostgreSQL 18 (Npgsql format: `Host=…;Port=5432;Database=StudentSync;Username=…;Password=…`); use the app's own login (owner of the database, not a superuser); add `SSL Mode=Require` when the database is on another host |
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
| `Sessions__StorePath` | | ✓ | Directory for signed-in sessions (tokens per session, encrypted with the Data Protection keys); the web image sets `/app/sessions`. Mount a persistent volume so restarts and redeploys keep people signed in. Empty = in memory (everyone is signed out on restart). One web node only; several nodes need a shared cache (Redis / PostgreSQL) |
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
* **Backups:** `pg_dump -Fc` (or a managed service's point-in-time recovery), plus the file storage volume and Data Protection keys. Restore drills should restore all three together. The free environment below has a nightly backup script; see there for restore steps.

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

## Free demo environment (Oracle Always Free, PostgreSQL on the API VM)

A zero-cost, publicly reachable environment on two Oracle Always Free micro VMs. Since 2026-11-15 the database is PostgreSQL 18 in a container on the API VM (owner decision; it replaced the Azure SQL free offer, whose auto-pause made the first sign-in after an idle hour take 45–60 s and which paused for the rest of the month once ~55 awake hours were used). It is still a single small server: see the limits below before relying on it.

| Piece | Where | Notes |
|---|---|---|
| Web (Blazor Server) + Caddy | Oracle `VM.Standard.E2.1.Micro` (x64, 1 GB) | Caddy terminates HTTPS for the app and API host names (automatic Let's Encrypt certificates) and proxies the API host to the API VM |
| API + PostgreSQL 18 | second `VM.Standard.E2.1.Micro` | `compose.api.yml`. Port 8080 is open to the VCN only; PostgreSQL listens on the VM's loopback only (SSH tunnel for maintenance). Uploads, Data Protection keys and the database live in Docker volumes on the boot disk |

**Why two micro VMs.** A tenancy's Always Free ARM allowance may already be used; the AMD micro shape is separate. Memory limits on the API VM: API 600 MB, PostgreSQL 256 MB (`shared_buffers=64MB`, `max_connections=40`), plus 2 GB swap from `cloud-init.yaml`.

**Database logins.** `postgres-init.sh` runs when the data volume is first created: it makes the `studentsync` login (no superuser, no create-role/database) and the `StudentSync` database owned by it. The API uses only that login (`api.env`); the `postgres` superuser (`postgres.env`) is for maintenance and backups.

**Background jobs.** `Jobs__Mode` defaults to `Hangfire` in `compose.api.yml` (reminders, doubt follow-up, exam finalising, recording retention, weekly digest). Set `JOBS_MODE=Inline` in `/opt/studentsync/.env` and re-run `docker compose -f compose.api.yml up -d` to turn the scheduler off if memory gets tight.

**Backups and restore.** `/etc/cron.d/studentsync-backup` runs `/opt/studentsync/backup.sh` nightly at 21:30 UTC: a `pg_dump -Fc` of `StudentSync` (checked with `pg_restore --list`) and a tarball of the `storage` volume, kept 14 days in `/opt/studentsync/backups`. When `/opt/studentsync/backup.env` sets `BACKUP_PAR_URL` (a write-only Object Storage pre-authenticated request ending in `/o/`; it expires and must be renewed), both files are also uploaded under the weekday name, giving a rolling 7 days off the VM. The Data Protection keys (`apikeys` volume) are not in the backup; losing them only invalidates outstanding file links. To restore a dump into an empty database:

```bash
docker compose -f compose.api.yml stop api
docker exec -i studentsync-postgres-1 pg_restore -U postgres -d StudentSync --clean --if-exists --no-owner --role=studentsync < backups/studentsync-db-<stamp>.dump
docker compose -f compose.api.yml start api
```

**Limits you accept**

* One VM holds the API, the database and the uploads: if the boot volume is lost, restore from the off-VM backup (up to a day of changes can be lost).
* Web-to-API traffic crosses the private subnet unencrypted. Signed-in sessions live on the `websessions` volume, so restarts and redeploys keep people signed in; deleting that volume or the `webkeys` volume signs everyone out.
* No SMTP. Oracle may reclaim Always Free instances that stay idle for 7 days.

**First-time set-up (summary)**

1. Oracle: a VCN with a public subnet; security list allowing 22, 80 and 443 from anywhere and 8080 from the VCN CIDR only (never 5432); two Ubuntu 24.04 Minimal micro instances launched with `cloud-init.yaml` (swap, Docker, Compose).
2. Host names: any DNS names pointing at the web VM. Without a domain, `app.<ip-with-dashes>.sslip.io` and `api.<ip-with-dashes>.sslip.io` work.
3. On the API VM create `/opt/studentsync/postgres.env` and `api.env`, on the web VM `web.env`, from the `*.example` files (`chmod 600`; the two database passwords must match). Keep generated secrets outside the repository.
4. `deploy/oracle/deploy.sh all` publishes both apps for linux-x64 on your machine, copies the output (plus `postgres-init.sh` and `backup.sh` for the API), builds the runtime image on each VM and starts the services. It needs `~/.studentsync-deploy/ssh_config` with hosts `ss-api` and `ss-web`.
5. Install the backup: `chmod 700 /opt/studentsync/backup.sh` and `echo '30 21 * * * root /opt/studentsync/backup.sh >> /var/log/studentsync-backup.log 2>&1' | sudo tee /etc/cron.d/studentsync-backup`; optionally create `backup.env` with `BACKUP_PAR_URL=…` (`chmod 600`).
6. Start empty with `docker compose -f compose.api.yml run --rm api create-institution …` (see *Release procedure*), or load the fictional demo data once (the seeder only runs in the Development environment, so the one-off container overrides it):

   ```bash
   printf 'ASPNETCORE_ENVIRONMENT=Development\nDemo__Password=<strong password>\nDemo__ForcePasswordChange=true\nEmail__Smtp__Host=\n' > seed.env
   docker compose -f compose.api.yml run --rm --env-from-file seed.env api seed-demo && rm seed.env
   ```

**Updating:** `deploy/oracle/deploy.sh [api|web|all]`. Migrations run when the API starts.

**Moving from the earlier Azure SQL database (done 2026-11-15).** The SQL Server data was copied into a local SQL Server, upgraded there with the last SQL Server migrations, then loaded into PostgreSQL table by table and compared row by row (identical except timestamps, which PostgreSQL stores to the microsecond rather than 100 ns). Password hashes and refresh tokens were copied unchanged, so nobody had to reset a password or sign in again. The SQL Server migrations remain in git history before the `InitialPostgreSql` migration.

**Before real use:** SMTP, durable off-VM file storage and a restore drill — see the checklist above.
