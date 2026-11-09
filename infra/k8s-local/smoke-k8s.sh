#!/bin/bash
# Smoke checks for the local kind cluster deployment — adapted from infra/docker/smoke-test.sh
# (same check structure, ingress hosts instead of published ports).
set -uo pipefail

API="http://api.aqt.localtest.me"
AUTH="http://auth.aqt.localtest.me"
APP="http://app.aqt.localtest.me"

fail=0
# Retries absorb the few seconds between a pod turning Ready and ingress-nginx
# propagating its endpoint (observed as a transient 503 right after helm --wait).
check() {
  local name="$1" url="$2" expect="$3"
  local code attempt
  for attempt in 1 2 3 4 5 6; do
    code=$(curl -s -m 15 -o /dev/null -w "%{http_code}" "$url")
    [ "$code" = "$expect" ] && break
    sleep 5
  done
  if [ "$code" = "$expect" ]; then
    echo "  ✓ $name ($code)"
  else
    echo "  ✗ $name — expected $expect got $code ($url)"
    fail=1
  fi
}

echo "[smoke-k8s] API health"
check "GET /health/live"  "$API/health/live"  200
check "GET /health/ready" "$API/health/ready" 200

echo "[smoke-k8s] AuthServer OIDC"
check "GET /health" "$AUTH/health" 200
disc=$(curl -s -m 15 "$AUTH/.well-known/openid-configuration")
if echo "$disc" | grep -q '"issuer"'; then
  echo "  ✓ OIDC discovery document has issuer"
else
  echo "  ✗ OIDC discovery missing issuer"
  fail=1
fi

echo "[smoke-k8s] Frontend"
check "GET / (index)" "$APP/" 200
# Same-origin API path through the web nginx proxy (api alias Service) —
# 401 is the healthy answer for an unauthenticated protected endpoint.
code=$(curl -s -m 15 -o /dev/null -w "%{http_code}" "$APP/api/v1/system/module-flags")
if [ "$code" = "401" ] || [ "$code" = "200" ]; then
  echo "  ✓ FE→API proxy path alive ($code)"
else
  echo "  ✗ FE→API proxy path — got $code"
  fail=1
fi

if [ "$fail" = "0" ]; then
  echo "[smoke-k8s] ALL GREEN"
else
  echo "[smoke-k8s] FAILURES — see above"
fi
exit $fail
