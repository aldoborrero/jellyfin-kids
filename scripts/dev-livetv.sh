#!/usr/bin/env bash
# Two live MPEG-TS channels for the dev server's M3U tuner, like the household's ErsatzTV: PekeTV on
# $JELLYFIN_KIDS_PORT + 10 and Grown-ups News on + 11, both on 127.0.0.1 (+ 1 stays free for `dev -p`). jellyfin-ffmpeg
# serves each over HTTP itself (-listen 1); it encodes only while Jellyfin is reading and starts again for the next
# connection.
set -euo pipefail

port=${JELLYFIN_KIDS_PORT:-8096}

# channel <name> <port> <tone Hz> <text>
channel() {
  local name=$1 listen=$2 tone=$3 text=$4
  while true; do
    ffmpeg -loglevel error -nostdin -re \
      -f lavfi -i "testsrc2=size=320x240:rate=25,drawtext=text='$text %{localtime}':x=8:y=8:fontsize=16:fontcolor=white:box=1:boxcolor=black" \
      -f lavfi -i "sine=frequency=$tone" \
      -c:v libx264 -preset ultrafast -tune zerolatency -g 50 -pix_fmt yuv420p -c:a aac -b:a 64k \
      -f mpegts -listen 1 "http://127.0.0.1:$listen/$name.ts" || true
    sleep 0.5
  done
}

channel peketv $((port + 10)) 440 PekeTV &
channel news $((port + 11)) 880 News &
trap 'kill $(jobs -p) 2>/dev/null' EXIT INT TERM
wait
