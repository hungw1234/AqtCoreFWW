#!/bin/bash
# Docker smoke test — run after docker compose up to validate all services
# Usage: bash infra/docker/smoke-test.sh
# Exit code 0 = all pass, non-zero = failures detected

set -e
PASS=0
FAIL=0

check() {
  local name="$1" url="$2" expected_status="$3"
  local status
  status=$(curl -s -o /dev/null -w "%{http_code}" --max-time 10 "$url" 2>/dev/null || echo "000")
  if [ "$status" = "$expected_status" ]; then
    echo "  ✓ $name — $status"
    PASS=$((PASS + 1))
  else
    echo "  ✗ $name — got $status, expected $expected_status"
    FAIL=$((FAIL + 1))
  fi
}

check_cors() {
  local name="$1" url="$2" origin="$3"
  local header
  header=$(curl -s -D - -o /dev/null --max-time 10 "$url" -H "Origin: $origin" 2>/dev/null | grep -i "access-control-allow-origin" || echo "")
  if echo "$header" | grep -qi "$origin"; then
    echo "  ✓ $name — CORS OK for $origin"
    PASS=$((PASS + 1))
  else
    echo "  ✗ $name — CORS missing for $origin"
    FAIL=$((FAIL + 1))
  fi
}

check_scope() {
  local url="http://localhost:5000/connect/authorize?client_id=aqtcorefw-spa-dev"
  url+="&redirect_uri=http%3A%2F%2Flocalhost%3A3000%2Fcallback"
  url+="&response_type=code&scope=openid+profile+email+api+offline_access"
  url+="&state=smoketest&code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM"
  url+="&code_challenge_method=S256"
  local status
  status=$(curl -s -o /dev/null -w "%{http_code}" --max-time 10 -L "$url" 2>/dev/null || echo "000")
  # 302 = redirect to login (scope accepted), 200 = login page rendered
  if [ "$status" = "302" ] || [ "$status" = "200" ]; then
    echo "  ✓ OIDC scope validation — $status (accepted)"
    PASS=$((PASS + 1))
  else
    echo "  ✗ OIDC scope validation — $status (rejected)"
    FAIL=$((FAIL + 1))
  fi
}

echo ""
echo "=== AqtCoreFW Docker Smoke Test ==="
echo ""

echo "--- Service health ---"
check "FE (web:3000)" "http://localhost:3000/" "200"
check "API (nginx:5001)" "http://localhost:5001/api/v1/feature-flags/EnablePdfWatermark" "200"
check "AuthServer (5000)" "http://localhost:5000/.well-known/openid-configuration" "200"
check "API health" "http://localhost:5001/health/ready" "200"
check "OpenAPI spec" "http://localhost:5001/openapi/v1.json" "200"

echo ""
echo "--- CORS validation ---"
check_cors "AuthServer CORS for :3000" "http://localhost:5000/.well-known/openid-configuration" "http://localhost:3000"
check_cors "AuthServer CORS for :5173" "http://localhost:5000/.well-known/openid-configuration" "http://localhost:5173"

echo ""
echo "--- OIDC scope validation ---"
check_scope

echo ""
echo "--- Auth enforcement ---"
check "401 on protected endpoint" "http://localhost:5001/api/v1/admin/feature-flags" "401"
check "Public masterdata" "http://localhost:5001/api/v1/masterdata/provinces" "200"

echo ""
echo "=== Results: $PASS passed, $FAIL failed ==="
[ "$FAIL" -eq 0 ] && echo "All checks passed!" || echo "FAILURES DETECTED"
exit $FAIL
