#!/bin/bash
set -e

echo "[TUNNEL-MANAGER] Downloading cloudflared"
curl -L https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-linux-amd64 -o ./cloudflared
chmod +x ./cloudflared

API_URL="https://tunnel-fetch.onrender.com/api/Tunnels"
API_KEY="HAT@123"

declare -A RUNNING_PID
declare -A RUNNING_KEY
declare -A FAIL_COUNT
declare -A COOLDOWN_UNTIL

is_port_listening() {
PORT=$1
ss -ltn | grep -q ":$PORT "
}

start_tunnel() {

HOST=$1
PORT=$2
ID=$3
SECRET=$4

BASE="$HOST:$PORT"
FULL="$HOST:$PORT:$ID:$SECRET"

NOW=$(date +%s)

if [[ -n "${COOLDOWN_UNTIL[$BASE]}" && ${COOLDOWN_UNTIL[$BASE]} -gt $NOW ]]; then
  echo "[TUNNEL-MANAGER] $BASE in cooldown until ${COOLDOWN_UNTIL[$BASE]}"
  return
fi

if is_port_listening "$PORT"; then
  echo "[TUNNEL-MANAGER] Port $PORT already in use, skipping start ($BASE)"
  return
fi

echo "[TUNNEL-MANAGER] Starting tunnel $BASE"

./cloudflared access tcp \
--loglevel error \
--hostname "$HOST" \
--url "localhost:$PORT" \
--service-token-id "$ID" \
--service-token-secret "$SECRET" \
> /dev/null 2>&1 &

PID=$!
sleep 2

if ps -p $PID > /dev/null; then
  RUNNING_PID[$BASE]=$PID
  RUNNING_KEY[$BASE]=$FULL
  FAIL_COUNT[$BASE]=0
  echo "[TUNNEL-MANAGER] Tunnel started successfully → $BASE (PID $PID)"
else
  FAIL_COUNT[$BASE]=$(( ${FAIL_COUNT[$BASE]:-0} + 1 ))

  echo "[TUNNEL-MANAGER] Tunnel start failed $BASE attempt ${FAIL_COUNT[$BASE]}"

  if [[ ${FAIL_COUNT[$BASE]} -ge 3 ]]; then
    COOLDOWN_UNTIL[$BASE]=$((NOW + 1800))
    echo "[TUNNEL-MANAGER] $BASE disabled for 30 minutes"
  fi
fi
}

cleanup_removed_tunnels() {

for BASE in "${!RUNNING_PID[@]}"
do
  if [[ -z "${CURRENT_TUNNELS[$BASE]}" ]]; then
    PID=${RUNNING_PID[$BASE]}
    echo "[TUNNEL-MANAGER] Removing tunnel $BASE (PID $PID)"
    kill $PID 2>/dev/null || true
    unset RUNNING_PID[$BASE]
    unset RUNNING_KEY[$BASE]
  fi
done

}

print_active_tunnels() {

echo "[TUNNEL-MANAGER] Active tunnels:"

if [ ${#RUNNING_PID[@]} -eq 0 ]; then
  echo "  (none)"
else
  for BASE in "${!RUNNING_PID[@]}"
  do
    echo "  - $BASE"
  done
fi

}

tunnel_manager() {

while true
do

echo "[TUNNEL-MANAGER] Fetching tunnels"

RESPONSE=$(curl -s --max-time 15 \
-H "X-Api-Key: $API_KEY" \
-H "Accept: application/json" \
"$API_URL")

if ! echo "$RESPONSE" | jq -e . >/dev/null 2>&1; then
  echo "[TUNNEL-MANAGER] Invalid API response"
  echo "[TUNNEL-MANAGER] Raw response:"
  echo "$RESPONSE"
  sleep 30
  continue
fi

declare -A CURRENT_TUNNELS

while read -r tunnel
do

HOST=$(echo "$tunnel" | jq -r '.cfHostname')
PORT=$(echo "$tunnel" | jq -r '.port')
ID=$(echo "$tunnel" | jq -r '.cfClientId')
SECRET=$(echo "$tunnel" | jq -r '.cfClientSecret')

BASE="$HOST:$PORT"
FULL="$HOST:$PORT:$ID:$SECRET"

CURRENT_TUNNELS[$BASE]=1

if [[ -n "${RUNNING_PID[$BASE]}" ]]; then
  PID=${RUNNING_PID[$BASE]}

  if ps -p $PID > /dev/null && is_port_listening "$PORT"; then
    continue
  else
    echo "[TUNNEL-MANAGER] Tunnel died → $BASE"
    kill $PID 2>/dev/null || true
    unset RUNNING_PID[$BASE]
  fi
fi

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

cleanup_removed_tunnels

print_active_tunnels

sleep 30

done

}

echo "[API] Starting ServiceStationApi"
dotnet ServiceStationApi.dll --urls "http://0.0.0.0:${PORT:-10000}" &

sleep 10

echo "[TUNNEL-MANAGER] Starting manager"
tunnel_manager