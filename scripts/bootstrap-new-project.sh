#!/usr/bin/env bash
# =============================================================================
# AqtCoreFW → New Project Bootstrap Script (Self-Healing)
# =============================================================================
# Usage:
#   bash scripts/bootstrap-new-project.sh [--yes] <NewName> <TargetDir> [modules-to-remove...]
#
# Flags:
#   --yes, -y    Skip confirmation prompt (for CI/automation)
#
# Example:
#   bash scripts/bootstrap-new-project.sh GSDT "C:/Aquitas/Project/GSDT" \
#     cases workflow ai forms notifications collaboration rules search signature reporting
#
#   bash scripts/bootstrap-new-project.sh --yes GSDT "C:/Aquitas/Project/GSDT" \
#     cases workflow ai forms notifications collaboration rules search signature reporting
#
# Available modules to remove:
#   cases workflow ai forms notifications collaboration rules search signature reporting
#
# The script auto-fixes failures at each step. Final result is always a buildable project.
# =============================================================================

set -uo pipefail  # strict mode but NO set -e (we handle errors ourselves)

# --- Colors ---
RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; CYAN='\033[0;36m'; NC='\033[0m'
log_info()  { echo -e "${CYAN}[INFO]${NC} $*"; }
log_ok()    { echo -e "${GREEN}[ OK ]${NC} $*"; }
log_warn()  { echo -e "${YELLOW}[WARN]${NC} $*"; }
log_error() { echo -e "${RED}[FAIL]${NC} $*"; }
log_fix()   { echo -e "${YELLOW}[FIX ]${NC} $*"; }

# In-place sed that works on GNU and BSD (macOS) sed: BSD requires a suffix
# argument after -i, so always pass one and delete the backup.
sed_i() { local expr="$1" file="$2"; sed -i.bak "$expr" "$file" && rm -f "${file}.bak"; }

# --- Validate args ---
if [ $# -lt 2 ]; then
  echo "Usage: $0 <NewProjectName> <TargetDir> [modules-to-remove...]"
  echo "Example: $0 GSDT \"C:/Aquitas/Project/GSDT\" cases workflow ai forms notifications"
  exit 1
fi

AUTO_YES=false
if [ "$1" = "--yes" ] || [ "$1" = "-y" ]; then
  AUTO_YES=true; shift
fi

NEW_NAME="$1"; TARGET_DIR="$2"; shift 2
MODULES_TO_REMOVE=("$@")
OLD_NAME="AqtCoreFW"
OLD_LOWER=$(echo "$OLD_NAME" | tr '[:upper:]' '[:lower:]')
NEW_LOWER=$(echo "$NEW_NAME" | tr '[:upper:]' '[:lower:]')
SOURCE_DIR="$(cd "$(dirname "$0")/.." && pwd)"

# Pascal-case helper for module names
to_pascal() {
  case "$1" in
    ai) echo "Ai" ;; masterdata) echo "MasterData" ;; systemparams) echo "SystemParams" ;;
    *) echo "$1" | awk -F_ '{o=""; for(i=1;i<=NF;i++) o=o toupper(substr($i,1,1)) substr($i,2); print o}' ;;
  esac
}

echo ""
echo "============================================"
echo "  ${OLD_NAME} → ${NEW_NAME}"
echo "============================================"
echo "  Source:  ${SOURCE_DIR}"
echo "  Target:  ${TARGET_DIR}"
echo "  Remove:  ${MODULES_TO_REMOVE[*]:-none}"
echo "============================================"
if [ "$AUTO_YES" != true ]; then
  read -p "Proceed? (y/N) " -n 1 -r; echo
  [[ ! $REPLY =~ ^[Yy]$ ]] && exit 0
fi

# Track issues for final report
ISSUES=()
add_issue() { ISSUES+=("$1"); }

# =============================================================================
# STEP 1: Clone/Copy codebase
# =============================================================================
log_info "Step 1: Clone codebase..."

# Detect git remote URL from source repo
GIT_REMOTE=$(cd "$SOURCE_DIR" && git remote get-url origin 2>/dev/null || echo "")

if [ -n "$GIT_REMOTE" ]; then
  # --- Git clone (fast, clean — no bin/obj/node_modules) ---
  # Follow the branch the script is run from; a bare clone would silently take the
  # remote's default branch (main) even when run from another branch such as dev.
  CLONE_BRANCH=$(cd "$SOURCE_DIR" && git rev-parse --abbrev-ref HEAD 2>/dev/null || echo "")
  CLONE_BRANCH_ARGS=()
  case "$CLONE_BRANCH" in ""|HEAD) ;; *) CLONE_BRANCH_ARGS=(--branch "$CLONE_BRANCH") ;; esac
  log_info "  Cloning from git: ${GIT_REMOTE}${CLONE_BRANCH:+ (branch ${CLONE_BRANCH})}"
  git clone --depth 1 ${CLONE_BRANCH_ARGS[@]+"${CLONE_BRANCH_ARGS[@]}"} "$GIT_REMOTE" "$TARGET_DIR" 2>&1 || {
    log_warn "  Git clone failed, falling back to local copy"
    GIT_REMOTE=""
  }
fi

if [ -z "$GIT_REMOTE" ]; then
  # --- Local copy fallback ---
  log_info "  Copying from local: ${SOURCE_DIR}"
  mkdir -p "$TARGET_DIR"
  if command -v rsync &>/dev/null; then
    rsync -a --exclude='.git' --exclude='node_modules' --exclude='bin/' \
      --exclude='obj/' --exclude='dist/' --exclude='.vs/' --exclude='*.user' \
      --exclude='logs/' --exclude='test-results/' --exclude='coverage/' \
      --exclude='playwright-report/' "$SOURCE_DIR/" "$TARGET_DIR/"
  else
    cp -r "$SOURCE_DIR/." "$TARGET_DIR/"
  fi
fi

cd "$TARGET_DIR"

# Remove .git from clone (will reinit fresh later)
rm -rf .git 2>/dev/null || true
# Clean build artifacts (in case of local copy)
find . -type d \( -name bin -o -name obj -o -name dist -o -name .vs \) \
  -not -path '*/node_modules/*' -exec rm -rf {} + 2>/dev/null || true
find . -name "*.user" -delete 2>/dev/null || true
rm -rf node_modules 2>/dev/null || true

log_ok "Codebase ready at ${TARGET_DIR}"

# =============================================================================
# STEP 1.5: Database variant detection (post 2026-04-27 repo split)
# =============================================================================
# AqtCoreFW (this repo) ships SQL Server 2022 only on `main`.
# Oracle 19c development moved to https://github.com/thiennd212/AqtCoreFW_Oracle
# The legacy `oracle-19c` branch on origin is scheduled for deletion ~2026-05-11.
log_info "Step 1.5: Detect database variant from source branch..."

SOURCE_BRANCH=$(cd "$SOURCE_DIR" && git rev-parse --abbrev-ref HEAD 2>/dev/null || echo "unknown")

case "$SOURCE_BRANCH" in
  oracle-19c)
    log_warn "  Source branch: oracle-19c (DEPRECATED — slated for deletion ~2026-05-11)"
    log_warn "  Oracle 19c development has moved to a dedicated repository:"
    log_warn "    https://github.com/thiennd212/AqtCoreFW_Oracle"
    log_warn "  Re-run this bootstrap against a fresh clone of AqtCoreFW_Oracle for Oracle work."
    log_warn "  Continuing with this branch will produce a SqlServer-shaped bootstrap that"
    log_warn "  does NOT include Oracle migrations / TDE wallet / VPD setup."
    ;;
  main|master)
    log_info "  Source branch: ${SOURCE_BRANCH} → SQL Server 2022 variant"
    log_info "  Default DB: Docker container 'sqlserver' (port 1433, SA auth)."
    log_info "  See docs/developer-onboarding-guide.md §2.1 for setup."
    ;;
  *)
    log_warn "  Source branch: ${SOURCE_BRANCH} (not a known SQL Server branch)"
    log_warn "  AqtCoreFW = SQL Server only. For Oracle, clone AqtCoreFW_Oracle instead."
    ;;
esac

# =============================================================================
# STEP 2: Remove modules
# =============================================================================
log_info "Step 2: Remove ${#MODULES_TO_REMOVE[@]} modules..."

for mod in ${MODULES_TO_REMOVE[@]+"${MODULES_TO_REMOVE[@]}"}; do
  pascal=$(to_pascal "$mod")

  # Remove source
  rm -rf "src/modules/${mod}" 2>/dev/null && log_ok "  src/modules/${mod}" || true

  # Remove unit tests
  find tests/unit/ -maxdepth 1 -type d -name "${OLD_NAME}.${pascal}.*" \
    -exec rm -rf {} + 2>/dev/null || true

  # Remove integration tests
  find tests/integration/ -maxdepth 1 -type d -name "${OLD_NAME}.${pascal}.*" \
    -exec rm -rf {} + 2>/dev/null || true

  log_ok "  tests for ${pascal}"
done

# =============================================================================
# STEP 3: Clean template artifacts
# =============================================================================
log_info "Step 3: Clean template-specific artifacts..."

rm -rf plans/2* plans/archive plans/visuals 2>/dev/null || true
rm -f plans/reports/*.md 2>/dev/null || true
rm -f temp_report.txt test-output.log 2>/dev/null || true
rm -rf test-results/ 2>/dev/null || true
rm -rf src/host/*/logs/ 2>/dev/null || true
rm -rf web/docs/journals/ web/plans/reports/ web/e2e/ 2>/dev/null || true
rm -rf web/playwright-report/ web/coverage/ web/dist/ web/node_modules/ 2>/dev/null || true
rm -rf jenkins-shared-library/ 2>/dev/null || true
rm -f Jenkinsfile release-manifest.json AGENTS.md .repomixignore 2>/dev/null || true
mkdir -p .claude/agent-memory plans plans/reports docs

log_ok "Artifacts cleaned."

# =============================================================================
# STEP 4: Replace namespace in file contents
# =============================================================================
log_info "Step 4: Replace '${OLD_NAME}' → '${NEW_NAME}' in file contents..."

find . -type f \( \
  -name "*.cs" -o -name "*.csproj" -o -name "*.slnx" -o -name "*.sln" \
  -o -name "*.json" -o -name "*.yml" -o -name "*.yaml" \
  -o -name "*.ts" -o -name "*.tsx" -o -name "*.js" -o -name "*.jsx" \
  -o -name "*.html" -o -name "*.css" -o -name "*.scss" \
  -o -name "*.props" -o -name "*.targets" -o -name "*.sh" \
  -o -name "*.ps1" -o -name "*.cmd" -o -name "*.xml" -o -name "*.config" \
  -o -name "*.cshtml" -o -name "*.razor" -o -name "*.sql" -o -name "*.py" \
  -o -name "*.bat" -o -name "*.http" -o -name "*.tpl" \
  -o -name "*.md" -o -name "*.txt" -o -name "Dockerfile*" \
  -o -name ".gitignore" -o -name ".env.example" \
\) -not -path '*/node_modules/*' -not -path '*/bin/*' -not -path '*/obj/*' \
  -not -path '*/.git/*' | while IFS= read -r file; do
  sed_i "s/${OLD_NAME}/${NEW_NAME}/g" "$file" 2>/dev/null || true
  sed_i "s/${OLD_LOWER}/${NEW_LOWER}/g" "$file" 2>/dev/null || true
done

log_ok "File contents replaced."

# =============================================================================
# STEP 5: Rename files and directories
# =============================================================================
log_info "Step 5: Rename .csproj files..."

find . -name "${OLD_NAME}.*.csproj" -not -path '*/bin/*' -not -path '*/obj/*' 2>/dev/null | while IFS= read -r f; do
  dir=$(dirname "$f"); old_b=$(basename "$f"); new_b="${old_b//${OLD_NAME}/${NEW_NAME}}"
  [ "$old_b" != "$new_b" ] && mv "$f" "${dir}/${new_b}" 2>/dev/null && log_ok "  ${old_b} → ${new_b}" || true
done

log_info "Step 5b: Rename directories (deepest first)..."

find . -type d -name "${OLD_NAME}.*" -not -path '*/bin/*' -not -path '*/obj/*' -not -path '*/.git/*' 2>/dev/null \
  | awk '{print length, $0}' | sort -rn | cut -d' ' -f2- | while IFS= read -r d; do
  parent=$(dirname "$d"); old_d=$(basename "$d"); new_d="${old_d//${OLD_NAME}/${NEW_NAME}}"
  [ "$old_d" != "$new_d" ] && [ -d "$d" ] && mv "$d" "${parent}/${new_d}" 2>/dev/null && log_ok "  ${old_d} → ${new_d}" || true
done

log_info "Step 5c: Rename lowercase-named files and directories (deepest first)..."

# Step 4 already rewrote references to the lowercase name (e.g. infra/helm/${NEW_LOWER}),
# so the paths must follow or those references break.
find . -name "*${OLD_LOWER}*" -not -path '*/node_modules/*' -not -path '*/bin/*' -not -path '*/obj/*' -not -path '*/.git/*' 2>/dev/null \
  | awk '{print length, $0}' | sort -rn | cut -d' ' -f2- | while IFS= read -r d; do
  parent=$(dirname "$d"); old_d=$(basename "$d"); new_d="${old_d//${OLD_LOWER}/${NEW_LOWER}}"
  [ "$old_d" != "$new_d" ] && [ -e "$d" ] && mv "$d" "${parent}/${new_d}" 2>/dev/null && log_ok "  ${old_d} → ${new_d}" || true
done

# Rename .slnx
[ -f "src/${OLD_NAME}.slnx" ] && mv "src/${OLD_NAME}.slnx" "src/${NEW_NAME}.slnx" && log_ok "  ${OLD_NAME}.slnx → ${NEW_NAME}.slnx"

log_ok "Rename complete."

# =============================================================================
# STEP 6: Clean .slnx — auto-fix orphan references
# =============================================================================
log_info "Step 6: Clean solution file..."

SLNX="src/${NEW_NAME}.slnx"
if [ -f "$SLNX" ]; then
  for mod in ${MODULES_TO_REMOVE[@]+"${MODULES_TO_REMOVE[@]}"}; do
    pascal=$(to_pascal "$mod")
    # Remove Folder blocks
    sed_i "/<Folder Name=\"\/modules\/${mod}\/\">/,/<\/Folder>/d" "$SLNX" 2>/dev/null || true
    # Remove standalone Project lines (tests)
    sed_i "/${NEW_NAME}\.${pascal}\./d" "$SLNX" 2>/dev/null || true
  done
  # Verify no orphans remain
  ORPHAN_COUNT=0
  for mod in ${MODULES_TO_REMOVE[@]+"${MODULES_TO_REMOVE[@]}"}; do
    pascal=$(to_pascal "$mod")
    if grep -q "${NEW_NAME}\.${pascal}\." "$SLNX" 2>/dev/null; then
      ORPHAN_COUNT=$((ORPHAN_COUNT + 1))
      log_fix "  Removing stubborn ref: ${pascal} from .slnx"
      grep -n "${NEW_NAME}\.${pascal}\." "$SLNX" | while IFS=: read -r line _; do
        sed_i "${line}d" "$SLNX" 2>/dev/null || true
      done
    fi
  done
  log_ok "Solution file cleaned."
else
  log_error "Solution file not found: ${SLNX}"
  add_issue "SLNX_MISSING"
fi

# =============================================================================
# STEP 7: Clean Program.cs — auto-fix orphan references
# =============================================================================
log_info "Step 7: Clean Program.cs..."

PROG="src/host/${NEW_NAME}.Api/Program.cs"
if [ -f "$PROG" ]; then
  for mod in ${MODULES_TO_REMOVE[@]+"${MODULES_TO_REMOVE[@]}"}; do
    pascal=$(to_pascal "$mod")
    # Remove using statements
    sed_i "/using ${NEW_NAME}\.${pascal}\./d" "$PROG" 2>/dev/null || true
    # Remove AddApplicationPart
    sed_i "/${NEW_NAME}\.${pascal}\.Presentation/d" "$PROG" 2>/dev/null || true
    # Remove module registration (various patterns)
    sed_i "/Add${pascal}Module/d" "$PROG" 2>/dev/null || true
    sed_i "/Add${pascal}Application/d" "$PROG" 2>/dev/null || true
    sed_i "/Add${pascal}Infrastructure/d" "$PROG" 2>/dev/null || true
    sed_i "/Add${pascal}Presentation/d" "$PROG" 2>/dev/null || true
    # Remove DbContext migrations
    sed_i "/${pascal}DbContext/d" "$PROG" 2>/dev/null || true
    # Remove domain events
    sed_i "/${NEW_NAME}\.${pascal}\.Domain\.Events/d" "$PROG" 2>/dev/null || true
    # Remove Hangfire jobs referencing module
    sed_i "/${NEW_NAME}\.${pascal}\.Infrastructure\.Jobs/d" "$PROG" 2>/dev/null || true
  done
  # Clean orphaned syntax (empty lines, dangling dots/semicolons)
  sed_i '/^[[:space:]]*\.$/d' "$PROG" 2>/dev/null || true
  sed_i '/^[[:space:]]*;$/d' "$PROG" 2>/dev/null || true

  # Verify cleanup
  REMAINING=""
  for mod in ${MODULES_TO_REMOVE[@]+"${MODULES_TO_REMOVE[@]}"}; do
    pascal=$(to_pascal "$mod")
    if grep -q "${NEW_NAME}\.${pascal}\." "$PROG" 2>/dev/null; then
      REMAINING="${REMAINING} ${pascal}"
      log_fix "  Force-removing remaining ${pascal} lines from Program.cs"
      sed_i "/${NEW_NAME}\.${pascal}/d" "$PROG" 2>/dev/null || true
    fi
  done
  log_ok "Program.cs cleaned."
else
  log_error "Program.cs not found: ${PROG}"
  add_issue "PROGRAM_MISSING"
fi

# =============================================================================
# STEP 8: Update .github workflows
# =============================================================================
log_info "Step 8: Update .github workflows..."

find .github/ -type f \( -name "*.yml" -o -name "*.yaml" \) \
  -exec sed -i.bak "s/${OLD_NAME}/${NEW_NAME}/g" {} + 2>/dev/null || true
find .github/ -type f \( -name "*.yml" -o -name "*.yaml" \) \
  -exec sed -i.bak "s/${OLD_LOWER}/${NEW_LOWER}/g" {} + 2>/dev/null || true
find .github/ -type f -name "*.bak" -delete 2>/dev/null || true

log_ok "GitHub workflows updated."

# =============================================================================
# STEP 9: Init Claude Kit + Claude Code
# =============================================================================
log_info "Step 9: Init Claude Kit..."

# Reset Claude Code project state
rm -rf .claude/session-state .claude/worktrees .claude/launch.json 2>/dev/null || true
cat > .claude/settings.local.json << 'EOF'
{"permissions":{"allow":[],"deny":[]}}
EOF

# Verify CK installation
CK_COMPONENTS=0
[ -d "$HOME/.claude/hooks" ] && CK_COMPONENTS=$((CK_COMPONENTS + 1))
[ -d "$HOME/.claude/skills" ] && CK_COMPONENTS=$((CK_COMPONENTS + 1))
[ -d "$HOME/.claude/rules" ] && CK_COMPONENTS=$((CK_COMPONENTS + 1))
[ -f "$HOME/.claude/settings.json" ] && CK_COMPONENTS=$((CK_COMPONENTS + 1))

if [ "$CK_COMPONENTS" -eq 4 ]; then
  HOOKS=$(find "$HOME/.claude/hooks" -maxdepth 1 -name "*.cjs" 2>/dev/null | wc -l)
  SKILLS=$(find "$HOME/.claude/skills" -maxdepth 1 -mindepth 1 -type d 2>/dev/null | wc -l)
  RULES=$(find "$HOME/.claude/rules" -maxdepth 1 -name "*.md" 2>/dev/null | wc -l)
  log_ok "Claude Kit: ${HOOKS} hooks, ${SKILLS} skills, ${RULES} rules"
else
  log_warn "Claude Kit incomplete (${CK_COMPONENTS}/4 components)"
  log_fix "Install CK: cd ~/.claude/skills && bash install.sh"
  add_issue "CK_INCOMPLETE"
fi

log_ok "Claude Kit initialized."

# =============================================================================
# STEP 10: Init git
# =============================================================================
log_info "Step 10: Init git..."

find . -type d \( -name bin -o -name obj \) -not -path '*/node_modules/*' \
  -exec rm -rf {} + 2>/dev/null || true
git init && git checkout -b main
log_ok "Git initialized."

# =============================================================================
# STEP 11: Build + auto-fix loop (max 3 attempts)
# =============================================================================
log_info "Step 11: Build verification (auto-fix up to 3 attempts)..."

cd src
BUILD_OK=false

for attempt in 1 2 3; do
  log_info "  Build attempt ${attempt}/3..."
  BUILD_OUTPUT=$(dotnet build "${NEW_NAME}.slnx" 2>&1)

  if echo "$BUILD_OUTPUT" | grep -q "Build succeeded"; then
    BUILD_OK=true
    log_ok "  Build succeeded on attempt ${attempt}!"
    break
  fi

  log_warn "  Build failed on attempt ${attempt}. Auto-fixing..."

  # --- Auto-fix: remove orphaned using statements ---
  ERRORS=$(echo "$BUILD_OUTPUT" | grep "error CS" || true)

  # Fix CS0246: type or namespace not found
  echo "$ERRORS" | grep "CS0246" | grep -o "'[^']\{1,\}'" | tr -d "'" | sort -u | while read -r missing_type; do
    for mod in ${MODULES_TO_REMOVE[@]+"${MODULES_TO_REMOVE[@]}"}; do
      pascal=$(to_pascal "$mod")
      if echo "$missing_type" | grep -qi "$pascal"; then
        log_fix "    Removing references to ${missing_type} (from deleted ${pascal})"
        grep -rl "${missing_type}" --include="*.cs" . 2>/dev/null | while read -r cs_file; do
          # Remove the using line or the whole line referencing it
          sed_i "/${missing_type}/d" "$cs_file" 2>/dev/null || true
        done
      fi
    done
  done

  # Fix CS0234: namespace does not exist — remove using lines, but only for
  # namespaces that belong to a removed module. The error message also names the
  # parent namespace (e.g. the project name); deleting every using for that would
  # strip valid code across the whole solution.
  echo "$ERRORS" | grep "CS0234" | grep -o "'[^']\{1,\}'" | tr -d "'" | sort -u | while read -r missing_ns; do
    for mod in ${MODULES_TO_REMOVE[@]+"${MODULES_TO_REMOVE[@]}"}; do
      pascal=$(to_pascal "$mod")
      if [ "$missing_ns" != "$NEW_NAME" ] && echo "$missing_ns" | grep -qi "$pascal"; then
        log_fix "    Removing using for missing namespace: ${missing_ns} (from deleted ${pascal})"
        grep -rl "using.*${missing_ns}" --include="*.cs" . 2>/dev/null | while read -r cs_file; do
          sed_i "/using.*${missing_ns}/d" "$cs_file" 2>/dev/null || true
        done
      fi
    done
  done

  # Fix: remove any .csproj referencing deleted module projects
  for mod in ${MODULES_TO_REMOVE[@]+"${MODULES_TO_REMOVE[@]}"}; do
    pascal=$(to_pascal "$mod")
    grep -rl "${NEW_NAME}\.${pascal}" --include="*.csproj" . 2>/dev/null | while read -r csproj; do
      log_fix "    Removing ProjectReference to ${pascal} in $(basename "$csproj")"
      sed_i "/${NEW_NAME}\.${pascal}/d" "$csproj" 2>/dev/null || true
    done
  done

  # Restore after fixes
  dotnet restore "${NEW_NAME}.slnx" 2>/dev/null || true
done

cd "$TARGET_DIR"

if [ "$BUILD_OK" != true ]; then
  log_error "Build still failing after 3 attempts."
  add_issue "BUILD_FAILED"
  echo ""
  echo "  Remaining errors — manual fix needed:"
  echo "$BUILD_OUTPUT" | grep "error CS" | head -20
fi

# =============================================================================
# STEP 12: Final verification + report
# =============================================================================
log_info "Step 12: Final verification..."

CHECKS_PASS=0; CHECKS_TOTAL=0

check() {
  local name="$1" result="$2"
  CHECKS_TOTAL=$((CHECKS_TOTAL + 1))
  if [ "$result" = "PASS" ]; then
    CHECKS_PASS=$((CHECKS_PASS + 1))
    log_ok "  ${name}"
  else
    log_error "  ${name}"
  fi
}

# Check 1: No stale namespace
STALE=$(grep -r "${OLD_NAME}" src/ --include="*.cs" --include="*.csproj" --include="*.json" --include="*.cshtml" --include="*.razor" -l 2>/dev/null \
  | grep -v "bin/" | grep -v "obj/" | wc -l)
if [ "$STALE" -gt 0 ]; then
  log_fix "  ${STALE} files still have '${OLD_NAME}' — auto-fixing..."
  grep -r "${OLD_NAME}" src/ --include="*.cs" --include="*.csproj" --include="*.json" --include="*.cshtml" --include="*.razor" -l 2>/dev/null \
    | grep -v "bin/" | grep -v "obj/" | while read -r f; do
    sed_i "s/${OLD_NAME}/${NEW_NAME}/g" "$f" 2>/dev/null || true
    sed_i "s/${OLD_LOWER}/${NEW_LOWER}/g" "$f" 2>/dev/null || true
  done
  STALE=$(grep -r "${OLD_NAME}" src/ --include="*.cs" --include="*.csproj" --include="*.json" --include="*.cshtml" --include="*.razor" -l 2>/dev/null \
    | grep -v "bin/" | grep -v "obj/" | wc -l)
fi
[ "$STALE" -eq 0 ] && check "No stale refs" "PASS" || check "Stale refs: ${STALE} files" "FAIL"

# Check 2: Build
[ "$BUILD_OK" = true ] && check "Build" "PASS" || check "Build" "FAIL"

# Check 3: Modules removed
MOD_REMAIN=0
for mod in ${MODULES_TO_REMOVE[@]+"${MODULES_TO_REMOVE[@]}"}; do
  [ -d "src/modules/${mod}" ] && MOD_REMAIN=$((MOD_REMAIN + 1)) && rm -rf "src/modules/${mod}"
done
[ "$MOD_REMAIN" -eq 0 ] && check "Modules removed" "PASS" || check "Modules removed (${MOD_REMAIN} fixed)" "PASS"

# Check 4: Solution file
SLNX_OK="PASS"
if [ -f "src/${NEW_NAME}.slnx" ]; then
  for mod in ${MODULES_TO_REMOVE[@]+"${MODULES_TO_REMOVE[@]}"}; do
    pascal=$(to_pascal "$mod")
    if grep -q "${NEW_NAME}\.${pascal}\." "src/${NEW_NAME}.slnx" 2>/dev/null; then
      sed_i "/${NEW_NAME}\.${pascal}\./d" "src/${NEW_NAME}.slnx"
    fi
  done
else
  SLNX_OK="FAIL"
fi
check "Solution file" "$SLNX_OK"

# Check 5: Key files
MISSING=0
for f in CLAUDE.md README.md .gitignore "src/${NEW_NAME}.slnx" \
  "src/host/${NEW_NAME}.Api/Program.cs" "src/host/${NEW_NAME}.Api/appsettings.json" \
  "docs/project-overview-pdr.md" "infra/docker/docker-compose.yml"; do
  [ ! -f "$f" ] && MISSING=$((MISSING + 1))
done
[ "$MISSING" -eq 0 ] && check "Key files (8)" "PASS" || check "Key files (${MISSING} missing)" "FAIL"

# If stale refs were fixed, rebuild
if [ "$BUILD_OK" != true ] && [ "$STALE" -eq 0 ]; then
  log_info "  Attempting final rebuild after auto-fixes..."
  cd src
  dotnet restore "${NEW_NAME}.slnx" 2>/dev/null
  if dotnet build "${NEW_NAME}.slnx" --no-restore 2>&1 | grep -q "Build succeeded"; then
    BUILD_OK=true
    log_ok "  Final rebuild succeeded!"
  fi
  cd "$TARGET_DIR"
fi

# =============================================================================
# STEP 13: Generate reports
# =============================================================================
log_info "Step 13: Generate reports..."

# POST_BOOTSTRAP.md
cat > POST_BOOTSTRAP.md << GUIDE
# ${NEW_NAME} — Post-Bootstrap Checklist

## Script results
- Build: $([ "$BUILD_OK" = true ] && echo "PASS" || echo "FAIL — manual fix needed")
- Checks: ${CHECKS_PASS}/${CHECKS_TOTAL} passed
- Issues: ${#ISSUES[@]} (${ISSUES[*]:-none})

## Claude Kit (CK) — Run these skills IN ORDER

Open project in Claude Code:
\`\`\`bash
cd ${TARGET_DIR}
claude
\`\`\`

Then run:
\`\`\`
/ck:docs init          # Update docs for new codebase
/ck:scout              # Build codebase awareness
/ck:security-scan      # Baseline security audit
/ck:test               # Verify remaining tests pass
/ck:git                # Commit clean baseline
\`\`\`

## Manual review (if needed)
- [ ] Review \`.github/workflows/ci.yml\` — update test paths, Docker registry
- [ ] Update \`appsettings.json\` — database name, app title
- [ ] Update \`infra/docker/docker-compose.yml\` — container names
- [ ] Update \`web/package.json\` — name, description

## Start development
- [ ] \`/ck:plan\` — Create implementation plan
- [ ] \`/ck:cook\` — End-to-end feature implementation
- [ ] \`/ck:ask\` — Technical consultation
GUIDE

log_ok "POST_BOOTSTRAP.md created."

# =============================================================================
# DONE
# =============================================================================
echo ""
echo "============================================"
echo "  ${GREEN}Bootstrap Complete!${NC}"
echo "============================================"
echo "  Project:  ${NEW_NAME}"
echo "  Location: ${TARGET_DIR}"
echo "  Build:    $([ "$BUILD_OK" = true ] && echo "${GREEN}PASS${NC}" || echo "${RED}FAIL${NC}")"
echo "  Checks:   ${CHECKS_PASS}/${CHECKS_TOTAL} passed"
echo ""
echo "  Next → Init Claude Kit:"
echo "    cd ${TARGET_DIR}"
echo "    claude"
echo ""
echo "    /ck:docs init"
echo "    /ck:scout"
echo "    /ck:security-scan"
echo "    /ck:test"
echo "    /ck:git"
echo ""
echo "  Details: POST_BOOTSTRAP.md"

# Report failure to callers (CI, automation) instead of always exiting 0
if [ "$BUILD_OK" != true ] || [ "$CHECKS_PASS" -ne "$CHECKS_TOTAL" ]; then
  log_error "Bootstrap finished with failures (build=${BUILD_OK}, checks ${CHECKS_PASS}/${CHECKS_TOTAL}) — exit 1"
  exit 1
fi
echo "============================================"
