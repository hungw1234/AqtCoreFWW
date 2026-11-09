#!/bin/bash
# Light synthetic load for the 24-48h soak (plan 260723-1258 P3 step 7) — enough traffic for
# the error-rate metric to have samples; deliberately NOT a load test (YAGNI: soak = stability).
# Preferred: run IN-CLUSTER so the loop survives host sleep / terminal close:
#   kubectl apply -f infra/k8s-local/soak-load-pod.yaml
# This host-side variant exists for ad-hoc runs only.
# Endpoints: health (always-200 baseline) + a real anonymous API GET (provinces is
# [AllowAnonymous] by design — public administrative catalog) + FE index.
set -u

API="http://api.aqt.localtest.me"
APP="http://app.aqt.localtest.me"

while true; do
  curl -s -m 15 -o /dev/null "$API/health/ready"
  curl -s -m 15 -o /dev/null "$API/health/live"
  curl -s -m 15 -o /dev/null "$API/api/v1/masterdata/provinces"   # AllowAnonymous → 200
  curl -s -m 15 -o /dev/null "$APP/"
  sleep 30
done
