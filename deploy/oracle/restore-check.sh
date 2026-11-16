#!/usr/bin/env bash
# Weekly restore test for the API VM (installed as /opt/studentsync/restore-check.sh, run by
# /etc/cron.d/studentsync-restore-check on Sundays after the nightly backup).
#
# Proves the newest backup can actually be restored: loads it into a throw-away database ("restore_check") in the same
# PostgreSQL container, checks that the key tables came back with rows, then drops the copy. The live database is
# never touched. The result is appended to /var/log/studentsync-restore-check.log and written to
# /opt/studentsync/restore-check.status ("ok …" or "FAILED …") so a failure is easy to spot.
set -euo pipefail

dir=/opt/studentsync/backups
status=/opt/studentsync/restore-check.status
container=studentsync-postgres-1
scratch=restore_check
latest=$(ls -1t "$dir"/studentsync-db-*.dump 2>/dev/null | head -1 || true)

fail() { echo "$(date -u +%FT%TZ) FAILED: $1" | tee "$status"; docker exec "$container" dropdb -U postgres --if-exists "$scratch" >/dev/null 2>&1 || true; exit 1; }

[ -n "$latest" ] || fail "no backup file found in $dir"
age_hours=$(( ( $(date +%s) - $(stat -c %Y "$latest") ) / 3600 ))
[ "$age_hours" -le 36 ] || fail "newest backup is $age_hours hours old ($latest)"

docker exec "$container" dropdb -U postgres --if-exists "$scratch"
docker exec "$container" createdb -U postgres "$scratch"
docker exec -i "$container" pg_restore -U postgres -d "$scratch" --no-owner --exit-on-error < "$latest" || fail "pg_restore could not load $latest"

counts=$(docker exec "$container" psql -U postgres -d "$scratch" -At -c \
  "select (select count(*) from \"Tenants\") || ' tenants, ' || (select count(*) from \"AspNetUsers\") || ' users, ' || (select count(*) from \"Sessions\") || ' classes, ' || (select count(*) from \"__EFMigrationsHistory\") || ' migrations'") \
  || fail "restored copy could not be read"
tenants=${counts%% tenants*}
[ "${tenants:-0}" -ge 1 ] || fail "restored copy has no institutes ($counts)"

docker exec "$container" dropdb -U postgres "$scratch"
echo "$(date -u +%FT%TZ) ok: $(basename "$latest") restored ($counts)" | tee "$status"
