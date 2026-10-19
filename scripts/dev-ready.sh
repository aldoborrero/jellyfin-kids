#!/usr/bin/env bash
# Readiness probe for the dev server: while starting, Jellyfin answers every path (even /health) with a status page,
# so wait for real JSON.
curl --silent --fail "http://127.0.0.1:${JELLYFIN_KIDS_PORT:-8096}/System/Info/Public" | jq --exit-status .Version >/dev/null
