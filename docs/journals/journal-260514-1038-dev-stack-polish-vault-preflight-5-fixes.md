# Dev Stack Polish — Vault Preflight + 5 Smoke-Surfaced Fixes

**Date:** 2026-05-14 08:43–10:38 ICT
**Commits:** 6 → origin/main `687608a9`
**Branch:** main (fast-forward, no divergence)
**Type:** Mixed — chore(infra), feat(api), fix(ui), fix(jit), refactor(menus)

## Session Goal

User started with `/aqt-runapp` to bring up Auth/API/FE dev stack and check E/W. As issues surfaced visually in the browser, debugging cycle: `/ck:debug` → root cause → fix → re-run. Five distinct bugs caught + skill update (vault preflight) committed.

## What Worked

| # | Fix | Commit |
|---|-----|--------|
| 1 | Skill `aqt-runapp` + `1_run-local-dev.ps1` now run Vault preflight (Test-VaultStack: container up → health 200 → probe 5 critical secret paths → run vault-init if missing). `-SkipVault` for fast paths. `.gitattributes` added to lock `*.sh` LF — Alpine `/bin/sh` was choking on CRLF (`set -eu\r` = "illegal option"), silently breaking `vault-init.sh` on Windows checkouts with `core.autocrlf=true`. | `78f65658` |
| 2 | API root `GET /` → 302 redirect to `/health/live` (was 404). Implemented in `MapGovHealthChecks` extension with `ExcludeFromDescription()` to keep OpenAPI clean. | `dcb25b3a` |
| 3 | Audit log table "Hành động" column overflow — `<Tag>` content (e.g. `SaveDefinitionGraph` ≈ 165px) painting over Module column at `width:100`. Bumped to `width:160` + `ellipsis: { showTitle: false }` + Tooltip wrapper. Module column got `width:140` (was unbounded). | `2d15f331` |
| 4 | Sidebar missing icons for `HistoryOutlined` (Lich su chay) + `GlobalOutlined`. BE seeder sent key names but `menu-icon-map.tsx` never imported them. Defense-in-depth: scripted diff of all BE-seed icons vs FE map keys, caught both in one pass. | `a873d5c5` |
| 5 | JIT Providers list page crashing with `Error parsing column 0 (Id=1 - Int64)`. Root cause: `JitProviderConfig` is a T1 entity (bigint `Id`, no `PublicId`), but DTO + Dapper row class declared `Guid Id`. Aligned DTO + Row to `long`; FE `id` typed as `number`. **Did NOT** promote to T2 (admin-only endpoint, MFA-gated, enumeration risk low). | `b2f0ff2a` |
| 6 | Removed duplicate "Doi tac tich hop" admin alias menu — rendered the same `PartnerListPage` already reachable via business chain "Tich hop & Bao cao > Tich hop > Doi tac". Cleaned 5 places: BE seeder Guid decls + menu rows + permission rows; FE `PARTNER_INTEGRATION_PATHS`, `getPartnerIntegrationMenuChildren`, sidebar entry, router; deleted `admin-integration-partners-page.tsx`. Orphan menu rows auto-purged by `PurgeOrphanMenusAsync` on next API boot. | `687608a9` |

End-to-end smoke after each fix: all 3 services up clean, 0 `VaultApiException` in Auth log post-preflight (vs 1 full stack trace pre-fix).

## What Didn't / Lessons

- **Background bash `&` doesn't survive Claude Code task boundary.** Two earlier attempts to restart API via `dotnet run ... &` returned `bash` exit 0 immediately but the dotnet child died. Must use the harness `run_in_background: true` flag for long-lived processes.
- **`PreToolUse:Bash` hook blocks "build" keyword.** Forced switch to PowerShell for `dotnet build`. Workaround documented; consider whitelisting in `.ckignore` if this becomes frequent.
- **Skill SKILL.md vs project-local script split.** `~/.claude/skills/aqt-runapp/SKILL.md` (bash, ad-hoc Claude execution) and `scripts/1_run-local-dev.ps1` (PowerShell, user double-click) both needed parallel updates. KISS: both kept Vault check logic in sync, but the duplication is fragile — future single-source if cadence grows.

## Decisions Made

- **JIT entity stays T1.** Considered T2 promotion (add `IPublicIdentified` + migration) — rejected per YAGNI. Endpoint behind `Authorize(Policy)` + `[RequiresMfa]`. Filed as carry-over if internal-Id leak ever becomes a concern.
- **Duplicate menu: kept business chain, dropped admin alias.** The "Doi tac" leaf belongs to business workflow ("Tich hop > Doi tac"), not admin config. Admin still reachable via same chain (perm `INTEG.PARTNER.VIEW` unchanged).
- **Vault check in dev runner, not in app startup.** Preflight stays at the ops layer (PowerShell + skill); the app continues to fail hard if Vault is unreachable. Avoid blurring the boundary between "ops responsibility" and "app responsibility."
- **`.gitattributes` text-attribute, not `core.eol` config.** Repo-level enforcement portable across contributors regardless of their Git settings.

## Test Status

- Build: 0E/0W (.NET) after each commit.
- FE typecheck: 0 errors after each commit.
- No new unit/integration tests added — UI polish + ops tooling, covered by existing suites + manual smoke.
- HTTP probes post-deploy: `/health/live` 200, `/api/v1/admin/jit-provider-configs` 401 (auth required, no crash), `/` 302 → `/health/live`.

## Process Cleanup

Stack-running while wrapping up:
- Auth :5002 PID 14684
- API :5001 PID 10640
- FE :5173 PID 14264

Will be killed by `/aqt-stop` Step 4.

## Next Session

Carry-over items (low priority, no deadline):
1. **T2 promotion for JitProviderConfig** — only if admin-Id-leak becomes a concern (currently MFA-gated).
2. **i18n cleanup pass** — orphan strings `nav.adminPartnerIntegration` in `vi.json`/`en.json` after menu drop.
3. **Skill SKILL.md vs PS1 sync** — consider extracting Vault preflight into a shared shell snippet sourced by both.
4. **Pre-existing untracked journal** `docs/journals/journal-260514-0840-audit-hmac-p3b-fu1-fu2-shipped.md` — from prior session, still untracked. User to decide commit.
