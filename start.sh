#!/bin/bash
set -e

echo "=== Downloading cloudflared ==="
curl -L https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-linux-amd64 -o ./cloudflared
chmod +x ./cloudflared

API_URL="https://tunnel-fetch-api.onrender.com/api/Tunnels"
API_KEY="HAT@123"

declare -A RUNNING

start_tunnel() {

./cloudflared access tcp \
--hostname "$1" \
--url "localhost:$2" \
--service-token-id "$3" \
--service-token-secret "$4" &
}

tunnel_manager() {

while true
do

echo "=== Fetching tunnels ==="

curl -s -H "X-Api-Key: $API_KEY" $API_URL | jq -c '.[]' | while read tunnel
do
  HOST=$(echo $tunnel | jq -r '.cfHostname')
  PORT=$(echo $tunnel | jq -r '.port')
  ID=$(echo $tunnel | jq -r '.cfClientId')
  SECRET=$(echo $tunnel | jq -r '.cfClientSecret')

  KEY="$HOST:$PORT"

  if [[ -z "${RUNNING[$KEY]}" ]]; then
      echo "Starting tunnel $HOST"

      start_tunnel "$HOST" "$PORT" "$ID" "$SECRET"

      RUNNING[$KEY]=1
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