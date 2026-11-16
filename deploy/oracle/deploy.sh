#!/usr/bin/env bash
# Redeploys Student Sync to the free demo environment (see DEPLOYMENT.md, "Free demo environment").
#
#   deploy/oracle/deploy.sh [api|web|all]      default: all
#
# The 1 GB VMs cannot run the .NET SDK, so each app is published here for linux-x64 and only the output is
# copied over; the VM then builds a small runtime image and restarts the service. Database migrations run when
# the API starts. Needs the .NET SDK locally and $STUDENTSYNC_DEPLOY_DIR/ssh_config (default
# ~/.studentsync-deploy) with the hosts "ss-api" and "ss-web". api.env / web.env already live on the VMs and are
# never copied from the repository.
set -euo pipefail

target="${1:-all}"
root="$(cd "$(dirname "$0")/../.." && pwd)"
cfg="${STUDENTSYNC_DEPLOY_DIR:-$HOME/.studentsync-deploy}/ssh_config"
out="$(mktemp -d)"
trap 'rm -rf "$out"' EXIT

ship() { # ship <name> <Project> <host> <extra files to copy next to the compose file...>
  local name="$1" project="$2" host="$3"; shift 3
  echo "==> $name: publishing"
  dotnet publish "$root/src/StudentSync.$project/StudentSync.$project.csproj" -c Release -r linux-x64 \
    --self-contained false -o "$out/$name" --nologo -v q
  echo "==> $name: copying to $host"
  ssh -F "$cfg" "$host" 'rm -rf /opt/studentsync/build/publish && mkdir -p /opt/studentsync/build/publish'
  COPYFILE_DISABLE=1 tar --no-xattrs -C "$out/$name" -czf - . | ssh -F "$cfg" "$host" 'tar -C /opt/studentsync/build/publish -xzf -'
  scp -q -F "$cfg" "$root/deploy/oracle/Dockerfile.runtime" "$host:/opt/studentsync/build/Dockerfile.runtime"
  scp -q -F "$cfg" "$root/deploy/oracle/compose.$name.yml" "$@" "$host:/opt/studentsync/"
  echo "==> $name: building image and restarting"
  ssh -F "$cfg" "$host" "cd /opt/studentsync \
    && docker build -q -f build/Dockerfile.runtime --build-arg APP_DLL=StudentSync.$project.dll -t studentsync-$name:demo build \
    && docker compose -f compose.$name.yml up -d \
    && docker image prune -f >/dev/null \
    && docker compose -f compose.$name.yml ps --format '{{.Service}}: {{.Status}}'"
}

api_files=("$root/deploy/oracle/postgres-init.sh" "$root/deploy/oracle/backup.sh" "$root/deploy/oracle/restore-check.sh")

case "$target" in
  api) ship api Api ss-api "${api_files[@]}" ;;
  web) ship web Web ss-web "$root/deploy/oracle/Caddyfile" ;;
  all) ship api Api ss-api "${api_files[@]}"; ship web Web ss-web "$root/deploy/oracle/Caddyfile" ;;
  *) echo "usage: $0 [api|web|all]" >&2; exit 2 ;;
esac
