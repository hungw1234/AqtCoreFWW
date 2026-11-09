#!/usr/bin/env bash
# AqtCoreFW - local dev runner for macOS/Linux.
#
# Thin wrapper: the single source of truth is scripts/1_run-local-dev.ps1 (pwsh 7,
# cross-platform). It starts SQL Server/Redis/RabbitMQ/Vault, seeds Vault (dev mode is
# in-memory, so every container restart needs a reseed), builds, and runs
# Auth :5002 / API :5001 / Frontend :5173. The previous standalone bash runner skipped
# the Vault step, which broke login.
#
# Usage (same arguments as the .ps1):
#   ./scripts/1_run-local-dev.sh                    # start all + monitor loop
#   ./scripts/1_run-local-dev.sh start fe
#   ./scripts/1_run-local-dev.sh restart api -SkipBuild
#   ./scripts/1_run-local-dev.sh stop all
# Legacy flags still accepted: --skip-build --fe-only --be-only --env <dev|test|prod>

set -eu

ROOT="$(cd "$(dirname "$0")/.." && pwd)"

if ! command -v pwsh >/dev/null 2>&1; then
  echo "[X] pwsh (PowerShell 7) not found. Install: brew install --cask powershell" >&2
  exit 1
fi

args=()
while [ $# -gt 0 ]; do
  case "$1" in
    --skip-build) args+=("-SkipBuild"); shift ;;
    --fe-only)    args+=("-FrontendOnly"); shift ;;
    --be-only)    args+=("-BackendOnly"); shift ;;
    --env)        args+=("-EnvMode" "${2:?--env needs dev|test|prod}"); shift 2 ;;
    *)            args+=("$1"); shift ;;
  esac
done

exec pwsh -NoProfile -File "$ROOT/scripts/1_run-local-dev.ps1" ${args[@]+"${args[@]}"}
