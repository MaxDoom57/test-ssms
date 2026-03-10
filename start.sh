#!/bin/bash
set -e

echo "=== Downloading cloudflared ==="
curl -L https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-linux-amd64 -o ./cloudflared
chmod +x ./cloudflared

API_URL="https://tunnel-fetch.onrender.com/api/Tunnels"
API_KEY="HAT@123"

declare -A RUNNING_PID
declare -A RUNNING_KEY
declare -A FAIL_COUNT
declare -A COOLDOWN_UNTIL

start_tunnel() {

HOST=$1
PORT=$2
ID=$3
SECRET=$4

BASE="$HOST:$PORT"
FULL="$HOST:$PORT:$ID:$SECRET"

NOW=$(date +%s)

# cooldown check
if [[ -n "${COOLDOWN_UNTIL[$BASE]}" && ${COOLDOWN_UNTIL[$BASE]} -gt $NOW ]]; then
  echo "Tunnel $BASE in cooldown until ${COOLDOWN_UNTIL[$BASE]}"
  return
fi

echo "Starting tunnel for $BASE"

./cloudflared access tcp \
--hostname "$HOST" \
--url "localhost:$PORT" \
--service-token-id "$ID" \
--service-token-secret "$SECRET" &

PID=$!
sleep 2

if ps -p $PID > /dev/null; then
  RUNNING_PID[$BASE]=$PID
  RUNNING_KEY[$BASE]=$FULL
  FAIL_COUNT[$BASE]=0
  echo "Tunnel started $BASE (PID $PID)"
else
  FAIL_COUNT[$BASE]=$(( ${FAIL_COUNT[$BASE]:-0} + 1 ))

  echo "Tunnel start failed for $BASE attempt ${FAIL_COUNT[$BASE]}"

  if [[ ${FAIL_COUNT[$BASE]} -ge 3 ]]; then
    COOLDOWN_UNTIL[$BASE]=$((NOW + 1800))
    echo "Tunnel $BASE disabled for 30 minutes"
  fi
fi
}

tunnel_manager() {

while true
do

echo "=== Fetching tunnels ==="

RESPONSE=$(curl -s --max-time 15 \
-H "X-Api-Key: $API_KEY" \
-H "Accept: application/json" \
"$API_URL")

# Validate JSON
if ! echo "$RESPONSE" | jq -e . >/dev/null 2>&1; then
  echo "Tunnel API returned invalid response"
  sleep 30
  continue
fi

while read -r tunnel
do

HOST=$(echo "$tunnel" | jq -r '.cfHostname')
PORT=$(echo "$tunnel" | jq -r '.port')
ID=$(echo "$tunnel" | jq -r '.cfClientId')
SECRET=$(echo "$tunnel" | jq -r '.cfClientSecret')

BASE="$HOST:$PORT"
FULL="$HOST:$PORT:$ID:$SECRET"

# check if already running
if [[ -n "${RUNNING_PID[$BASE]}" ]]; then
  PID=${RUNNING_PID[$BASE]}

  if ps -p $PID > /dev/null; then
    continue
  else
    echo "Tunnel process died for $BASE"
    unset RUNNING_PID[$BASE]
  fi
fi

# start or restart tunnel
start_tunnel "$HOST" "$PORT" "$ID" "$SECRET"

done < <(
echo "$RESPONSE" | jq -c '.[] |
select(
  .cfHostname != null and .cfHostname != "" and
  .cfClientId != null and .cfClientId != "" and
  .cfClientSecret != null and .cfClientSecret != "" and
  .port != null
)'
)

sleep 30

done

}

echo "=== Starting ServiceStationApi ==="
dotnet ServiceStationApi.dll --urls "http://0.0.0.0:${PORT:-10000}" &

sleep 10

echo "=== Starting tunnel manager ==="
tunnel_manager