#!/usr/bin/env bash
# Run the dev Jellyfin server with its state under $JELLYFIN_KIDS_DEV, listening on localhost only, on
# $JELLYFIN_KIDS_PORT (default 8096) so several dev loops can run side by side.
set -euo pipefail

root=$(cd "$(dirname "$0")/.." && pwd)
dev=${JELLYFIN_KIDS_DEV:-$root/.dev}
port=${JELLYFIN_KIDS_PORT:-8096}
mkdir -p "$dev/data" "$dev/config" "$dev/cache" "$dev/log"

# The dev admin has a throwaway password, so keep the server off the network.
# The Android emulator still reaches it at http://10.0.2.2:<port>.
network=$dev/config/network.xml
if [[ ! -f $network ]]; then
  cat >"$network" <<XML
<?xml version="1.0" encoding="utf-8"?>
<NetworkConfiguration xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <LocalNetworkAddresses>
    <string>127.0.0.1</string>
  </LocalNetworkAddresses>
  <EnableUPnP>false</EnableUPnP>
  <InternalHttpPort>$port</InternalHttpPort>
</NetworkConfiguration>
XML
elif grep -q '<InternalHttpPort>' "$network"; then
  sed -i "s|<InternalHttpPort>[0-9]*</InternalHttpPort>|<InternalHttpPort>$port</InternalHttpPort>|" "$network"
else
  sed -i "s|</NetworkConfiguration>|  <InternalHttpPort>$port</InternalHttpPort>\n</NetworkConfiguration>|" "$network"
fi

exec jellyfin \
  --datadir "$dev/data" \
  --configdir "$dev/config" \
  --cachedir "$dev/cache" \
  --logdir "$dev/log" \
  --nonetchange
