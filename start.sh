#!/bin/bash
set -e

echo "=== Downloading cloudflared ==="
curl -L https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-linux-amd64 -o ./cloudflared
chmod +x ./cloudflared

API_URL="https://tunnel-fetch-api.onrender.com/api/Tunnels"
API_KEY="HAT@123"

declare -A RUNNING_PID
declare -A RUNNING_KEY

start_tunnel() {

HOST=$1
PORT=$2
ID=$3
SECRET=$4

./cloudflared access tcp \
--hostname "$HOST" \
--url "localhost:$PORT" \
--service-token-id "$ID" \
--service-token-secret "$SECRET" &

PID=$!

BASE="$HOST:$PORT"
FULL="$HOST:$PORT:$ID:$SECRET"

RUNNING_PID[$BASE]=$PID
RUNNING_KEY[$BASE]=$FULL

echo "Tunnel started $BASE (PID $PID)"
}

tunnel_manager() {

while true
do

echo "=== Fetching tunnels ==="

curl -s -H "X-Api-Key: $API_KEY" "$API_URL" | jq -c '.[]' | while read tunnel
do
  HOST=$(echo $tunnel | jq -r '.cfHostname')
  PORT=$(echo $tunnel | jq -r '.port')
  ID=$(echo $tunnel | jq -r '.cfClientId')
  SECRET=$(echo $tunnel | jq -r '.cfClientSecret')

  BASE="$HOST:$PORT"
  FULL="$HOST:$PORT:$ID:$SECRET"

  if [[ -z "${RUNNING_KEY[$BASE]}" ]]; then

      echo "Starting new tunnel $BASE"
      start_tunnel "$HOST" "$PORT" "$ID" "$SECRET"

  elif [[ "${RUNNING_KEY[$BASE]}" != "$FULL" ]]; then

      echo "Tunnel config changed for $BASE"

      OLD_PID=${RUNNING_PID[$BASE]}
      echo "Stopping old tunnel PID $OLD_PID"

      kill $OLD_PID || true

      start_tunnel "$HOST" "$PORT" "$ID" "$SECRET"

  fi

done

sleep 30

done

}

echo "=== Starting ServiceStationApi ==="
dotnet ServiceStationApi.dll --urls "http://0.0.0.0:${PORT:-10000}" &

sleep 10

echo "=== Starting tunnel manager ==="
tunnel_manager