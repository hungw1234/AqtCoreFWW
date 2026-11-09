#!/usr/bin/env bash
# run-migrations.sh — Run EF Core database update for every module + shared DbContext.
# Usage: ./infra/scripts/run-migrations.sh [Development|Staging|Production]
#
# Note: by default (SKIP_STARTUP_MIGRATIONS unset) Program.cs invokes MigrateAsync on all 19 contexts
# at API host startup (idempotent). When the host runs with SKIP_STARTUP_MIGRATIONS=true (prod default —
# see docs/deployment-guide.md §4), migrations must come from elsewhere: either the Helm `db-migrate`
# Job (`dotnet AqtCoreFW.Api.dll --migrate-only`) or this script. Also handy for CI smoke checks,
# partial container DB rebuild, or schema-only verification without starting the host.
set -euo pipefail

ENVIRONMENT=${1:-Development}
REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
HOST_PROJECT="$REPO_ROOT/src/host/AqtCoreFW.Api"

export ASPNETCORE_ENVIRONMENT="$ENVIRONMENT"

echo "==> Running EF Core migrations (environment: $ENVIRONMENT)"
echo "    Repo root: $REPO_ROOT"
echo "    Startup project: $HOST_PROJECT"

# Ordered list — earlier contexts have FK targets later contexts depend on.
# Format: "ContextName|relative/path/to/csproj"
# 14 modules + 5 shared infra (gateway/webhooks/messaging/alerting/backup) = 19 contexts.
CONTEXTS=(
  "ApiKeyDbContext|src/shared/AqtCoreFW.Infrastructure"
  "IdentityDbContext|src/modules/identity/AqtCoreFW.Identity.Infrastructure"
  "AiDbContext|src/modules/ai/AqtCoreFW.Ai.Infrastructure"
  "AuditDbContext|src/modules/audit/AqtCoreFW.Audit.Infrastructure"
  "FilesDbContext|src/modules/files/AqtCoreFW.Files.Infrastructure"
  "FormsDbContext|src/modules/forms/AqtCoreFW.Forms.Infrastructure"
  "MasterDataDbContext|src/modules/masterdata/AqtCoreFW.MasterData.Infrastructure"
  "NotificationsDbContext|src/modules/notifications/AqtCoreFW.Notifications.Infrastructure"
  "OrgDbContext|src/modules/organization/AqtCoreFW.Organization.Infrastructure"
  "SystemParamsDbContext|src/modules/systemparams/AqtCoreFW.SystemParams.Infrastructure"
  "WorkflowDbContext|src/modules/workflow/AqtCoreFW.Workflow.Infrastructure"
  "ReportingDbContext|src/modules/reporting/AqtCoreFW.Reporting.Infrastructure"
  "WebhookDbContext|src/shared/AqtCoreFW.Infrastructure"
  "BackupDbContext|src/shared/AqtCoreFW.Infrastructure"
  "AlertingDbContext|src/shared/AqtCoreFW.Infrastructure"
  "RulesDbContext|src/modules/rules/AqtCoreFW.Rules.Infrastructure"
  "CollaborationDbContext|src/modules/collaboration/AqtCoreFW.Collaboration.Infrastructure"
  "IntegrationDbContext|src/modules/integration/AqtCoreFW.Integration.Infrastructure"
  "MessagingDbContext|src/shared/AqtCoreFW.Infrastructure"
)

FAILED=()

for entry in "${CONTEXTS[@]}"; do
  ctx="${entry%%|*}"
  proj_rel="${entry#*|}"
  project_dir="$REPO_ROOT/$proj_rel"

  if [[ ! -d "$project_dir" ]]; then
    echo "  [WARN] Project directory not found for $ctx: $project_dir — skipping"
    FAILED+=("$ctx (directory missing)")
    continue
  fi

  echo "--> Migrating $ctx (project: $proj_rel)"
  # Use `dotnet-ef` directly — `dotnet ef` resolves the local manifest first and
  # refuses to fall back to the global tool when manifest restore is incomplete.
  if dotnet-ef database update \
        --context "$ctx" \
        --project "$project_dir" \
        --startup-project "$HOST_PROJECT" \
        --no-build; then
    echo "    OK: $ctx"
  else
    echo "    FAILED: $ctx"
    FAILED+=("$ctx")
  fi
done

if [[ ${#FAILED[@]} -gt 0 ]]; then
  echo ""
  echo "==> Some migrations failed:"
  for f in "${FAILED[@]}"; do
    echo "    - $f"
  done
  exit 1
fi

echo ""
echo "==> All 19 migrations completed successfully."
echo "==> Next: ./infra/scripts/apply-page-compression.sql via sqlcmd to enable page compression."
