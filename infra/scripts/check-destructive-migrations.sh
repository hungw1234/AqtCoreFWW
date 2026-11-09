#!/usr/bin/env bash
# =============================================================================
# check-destructive-migrations.sh
# DevSecOps CI gate — detect destructive SQL in EF migration files.
#
# Scans all *.cs migration files changed in the current PR/commit for
# patterns that indicate potentially destructive DDL operations:
#   DROP TABLE, DROP COLUMN, ALTER COLUMN (type change), TRUNCATE TABLE
#
# Exit codes:
#   0 — no destructive patterns found (gate passes)
#   1 — destructive patterns detected (gate warns; set BLOCK_DESTRUCTIVE=true to fail)
#
# Usage in CI:
#   bash infra/scripts/check-destructive-migrations.sh
#   BLOCK_DESTRUCTIVE=true bash infra/scripts/check-destructive-migrations.sh
# =============================================================================
set -euo pipefail

BLOCK_DESTRUCTIVE="${BLOCK_DESTRUCTIVE:-false}"
MIGRATIONS_DIR="${MIGRATIONS_DIR:-src}"
FOUND=0

echo "=== Destructive Migration Gate ==="
echo "Scanning: $MIGRATIONS_DIR/**/Migrations/**/*.cs"
echo ""

# Patterns to flag (case-insensitive)
PATTERNS=(
    "migrationBuilder\.DropTable"
    "migrationBuilder\.DropColumn"
    "migrationBuilder\.AlterColumn"
    "migrationBuilder\.DropIndex"
    "migrationBuilder\.DropForeignKey"
    "TRUNCATE\s+TABLE"
    "DROP\s+TABLE"
    "DROP\s+COLUMN"
)

# Find migration files (exclude Designer.cs snapshots)
MIGRATION_FILES=$(find "$MIGRATIONS_DIR" \
    -path "*/Migrations/*.cs" \
    ! -name "*Designer.cs" \
    ! -name "*ModelSnapshot.cs" \
    2>/dev/null || true)

if [ -z "$MIGRATION_FILES" ]; then
    echo "No migration files found. Gate passed."
    exit 0
fi

for FILE in $MIGRATION_FILES; do
    for PATTERN in "${PATTERNS[@]}"; do
        MATCHES=$(grep -inE "$PATTERN" "$FILE" 2>/dev/null || true)
        if [ -n "$MATCHES" ]; then
            echo "::warning file=$FILE::Destructive migration pattern detected: $PATTERN"
            echo "  File: $FILE"
            echo "  Match: $MATCHES"
            echo ""
            FOUND=$((FOUND + 1))
        fi
    done
done

if [ "$FOUND" -gt 0 ]; then
    echo "=== $FOUND destructive migration pattern(s) found ==="
    echo "Review each migration carefully before merging."
    echo "Ensure data is backed up and rollback plan is documented."

    if [ "$BLOCK_DESTRUCTIVE" = "true" ]; then
        echo "::error::Destructive migrations detected — blocking merge (BLOCK_DESTRUCTIVE=true)"
        exit 1
    else
        echo "::warning::Destructive migrations detected — manual review required"
        exit 0
    fi
else
    echo "=== No destructive migrations found. Gate passed. ==="
    exit 0
fi
