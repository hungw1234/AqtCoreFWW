#!/bin/bash
# Soak cadence check (plan 260723-1258 P3 step 7) — run every 4-6h during the 24-48h soak.
# Appends one CSV row per run to infra/k8s-local/soak-log.csv (gitignored) and prints a
# human summary. Pass criteria (phase file): 0 unexpected pod restarts; memory not
# monotonically trending >20%/24h; ~0 synthetic 5xx; Hangfire jobs on schedule; the two
# alerting self-monitor alerts silent; Prometheus targets continuously up.
# Requires: kubectl context on the kind cluster; Prometheus reachable via port-forward
# (this script opens/closes its own on 127.0.0.1:19090).
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
LOG="$REPO_ROOT/infra/k8s-local/soak-log.csv"
TS="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

# --- Prometheus port-forward (self-managed) -----------------------------------
kubectl -n monitoring port-forward svc/kps-kube-prometheus-stack-prometheus 19090:9090 > /dev/null 2>&1 &
PF_PID=$!
trap 'kill $PF_PID 2>/dev/null' EXIT
sleep 3

promq() {  # promq '<promql>' [empty_as] → first value; empty result → $2 (default "na").
  # count()/sum() over a fully-empty selection return an EMPTY vector, not 0 — callers
  # counting bad-things pass 0 so "nothing wrong" reads as 0, not na (probed 2026-07-23).
  local empty="${2:-na}"
  curl -s -m 10 "http://127.0.0.1:19090/api/v1/query" --data-urlencode "query=$1" \
    | python -c "import sys,json;
d=json.load(sys.stdin); r=d.get('data',{}).get('result',[])
print(r[0]['value'][1] if r else '$empty')" 2>/dev/null || echo "na"
}

# --- Signals ------------------------------------------------------------------
READY=$(curl -s -m 15 -o /dev/null -w "%{http_code}" http://api.aqt.localtest.me/health/ready)
# awk, not bc — Git Bash on this machine has no bc (probed 2026-07-23). The soak-load
# curl pod is excluded: an OOM-restart of the load generator is not an app-stability signal.
RESTARTS=$(kubectl -n aqt-app get pods -l 'app!=soak-load' -o jsonpath='{range .items[*]}{.status.containerStatuses[0].restartCount}{"\n"}{end}' 2>/dev/null | awk '{s+=$1} END {print s+0}')
# 5xx rate over the last hour (prometheus-net pipeline); empty selection = no 5xx = 0
RATE5XX=$(promq 'sum(rate(http_requests_received_total{code=~"5.."}[1h]))' 0)
# API pod working-set memory (bytes)
MEM=$(promq 'max(container_memory_working_set_bytes{namespace="aqt-app",container="aqtcorefw"})')
# Targets down count (empty = all up = 0)
DOWN=$(promq 'count(up == 0)' 0)
# Alerting self-monitor alerts (must stay 0; empty = none firing = 0)
SELFMON=$(promq 'count(ALERTS{alertname=~"AqtCoreFWAlertEvaluationFailing|AqtCoreFWAlertNotificationFailing",alertstate="firing"})' 0)
# RabbitMQ connection count from app pods (P2 verification 2 long-run re-check).
# NOT `grep -c ... || echo na`: grep -c prints "0" AND exits 1 on zero matches, so the ||
# branch would emit a SECOND line ("0\nna") and corrupt the CSV exactly when the signal
# goes bad (review finding). Capture the exec output first, test it, then count.
RMQ_RAW=$(kubectl -n aqt-data exec deploy/rabbitmq -- rabbitmqctl list_connections peer_host 2>/dev/null)
if [ -n "$RMQ_RAW" ]; then RMQCONN=$(printf '%s\n' "$RMQ_RAW" | grep -c "10.244"); else RMQCONN=na; fi
# Hangfire: most recent succeeded job age (seconds) via SQL — na if query path unavailable
SQLPOD=$(kubectl -n aqt-data get pod -l app=sqlserver -o jsonpath='{.items[0].metadata.name}' 2>/dev/null)
SA_PW=$(kubectl -n aqt-data get secret aqt-data-secrets -o jsonpath='{.data.sa-password}' 2>/dev/null | base64 -d)
# Table probed live 2026-07-23: schema is [hangfire].[Job] (StateName/CreatedAt columns).
HANGFIRE=$(MSYS_NO_PATHCONV=1 kubectl -n aqt-data exec "$SQLPOD" -- env SA_PW="$SA_PW" sh -c \
  '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SA_PW" -d AqtCoreFW -C -h -1 -W -Q "SET NOCOUNT ON; SELECT DATEDIFF(SECOND, MAX(CreatedAt), SYSUTCDATETIME()) FROM [hangfire].[Job] WHERE StateName=N'"'"'Succeeded'"'"';"' 2>/dev/null | tr -d '[:space:]' || echo na)

# --- Emit ---------------------------------------------------------------------
[ -f "$LOG" ] || echo "ts,ready_http,pod_restarts,rate5xx_1h,api_mem_bytes,targets_down,selfmon_firing,rmq_conns,hangfire_last_ok_age_s" > "$LOG"
echo "$TS,$READY,$RESTARTS,$RATE5XX,$MEM,$DOWN,$SELFMON,$RMQCONN,$HANGFIRE" >> "$LOG"

echo "[soak-check] $TS"
echo "  ready=$READY restarts=$RESTARTS 5xx/h=$RATE5XX mem=$MEM down_targets=$DOWN selfmon=$SELFMON rmq_conns=$RMQCONN hangfire_age_s=$HANGFIRE"
echo "  log: $LOG"
