#!/usr/bin/env bash
# Nightly backup for the API VM (installed as /opt/studentsync/backup.sh, run by /etc/cron.d/studentsync-backup).
#
#  - PostgreSQL: custom-format pg_dump of the StudentSync database, checked with pg_restore --list.
#  - Uploaded files: tar of the API "storage" volume (photos, school reports, attachments).
#  - Keeps 14 days on the VM. When /opt/studentsync/backup.env sets BACKUP_PAR_URL (a write-only Oracle Object
#    Storage pre-authenticated request ending in "/o/"), also uploads both files there under the weekday name, so the
#    bucket holds a rolling 7 days off the VM.
#
# Restore: see DEPLOYMENT.md, "Free demo environment" -> "Backups and restore".
set -euo pipefail

dir=/opt/studentsync/backups
stamp=$(date -u +%Y%m%dT%H%M%SZ)
day=$(date -u +%a)
db="$dir/studentsync-db-$stamp.dump"
files="$dir/studentsync-files-$stamp.tar.gz"
mkdir -p "$dir"
chmod 700 "$dir"

docker exec studentsync-postgres-1 pg_dump -U postgres -d StudentSync -Fc > "$db.tmp"
docker exec -i studentsync-postgres-1 pg_restore --list < "$db.tmp" > /dev/null
mv "$db.tmp" "$db"

storage=$(docker volume inspect studentsync_storage --format '{{.Mountpoint}}')
tar -C "$storage" -czf "$files.tmp" .
mv "$files.tmp" "$files"
chmod 600 "$db" "$files"

find "$dir" -name 'studentsync-*' -mtime +14 -delete

if [ -f /opt/studentsync/backup.env ]; then
  # shellcheck disable=SC1091
  . /opt/studentsync/backup.env
  curl -fsS --retry 3 -X PUT --upload-file "$db" "${BACKUP_PAR_URL}studentsync-db-$day.dump"
  curl -fsS --retry 3 -X PUT --upload-file "$files" "${BACKUP_PAR_URL}studentsync-files-$day.tar.gz"
fi

echo "$(date -u +%FT%TZ) backup ok: $(du -h "$db" | cut -f1) database, $(du -h "$files" | cut -f1) files"
