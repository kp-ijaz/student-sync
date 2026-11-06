#!/usr/bin/env bash
# Runs the read-only demo walkthrough on a booted iOS simulator and saves a simulator
# screenshot (xcrun simctl io <device> screenshot) at every "SHOT:<name>" marker.
#
# Usage (from mobile/student_sync):
#   DEMO_PASSWORD=... scripts/capture_screenshots.sh <simulator-udid> [api-base-url]
#
# DEMO_PASSWORD is the development-only value from the repository's .env file; it is
# passed on the command line and never written to the repo.
set -euo pipefail

DEVICE="${1:?simulator udid required}"
API="${2:-http://localhost:5080/api/v1}"
OUT="docs/screenshots"
: "${DEMO_PASSWORD:?set DEMO_PASSWORD}"
mkdir -p "$OUT"

flutter test integration_test/demo_walkthrough_test.dart -d "$DEVICE" \
  --dart-define=API_BASE_URL="$API" --dart-define=DEMO_PASSWORD="$DEMO_PASSWORD" 2>&1 |
while IFS= read -r line; do
  echo "$line"
  if [[ "$line" == *"SHOT:"* ]]; then
    name="${line##*SHOT:}"
    name="${name//[^A-Za-z0-9_]/}"
    xcrun simctl io "$DEVICE" screenshot "$OUT/$name.png" >/dev/null 2>&1 && echo "  -> saved $OUT/$name.png"
  fi
done
