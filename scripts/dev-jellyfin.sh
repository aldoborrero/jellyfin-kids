#!/usr/bin/env bash
# Run the dev Jellyfin server with its state under $JELLYFIN_KIDS_DEV, listening on localhost only.
set -euo pipefail

root=$(cd "$(dirname "$0")/.." && pwd)
dev=${JELLYFIN_KIDS_DEV:-$root/.dev}
mkdir -p "$dev/data" "$dev/config" "$dev/cache" "$dev/log"

# The dev admin has a throwaway password, so keep the server off the network.
# The Android emulator still reaches it at http://10.0.2.2:8096.
if [[ ! -f $dev/config/network.xml ]]; then
  cat >"$dev/config/network.xml" <<'XML'
<?xml version="1.0" encoding="utf-8"?>
<NetworkConfiguration xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <LocalNetworkAddresses>
    <string>127.0.0.1</string>
  </LocalNetworkAddresses>
  <EnableUPnP>false</EnableUPnP>
</NetworkConfiguration>
XML
fi

exec jellyfin \
  --datadir "$dev/data" \
  --configdir "$dev/config" \
  --cachedir "$dev/cache" \
  --logdir "$dev/log" \
  --nonetchange
