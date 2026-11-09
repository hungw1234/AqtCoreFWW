# Journal — Tenant-Isolation Sweep + ExternalIdentities/LoginAudit Cross-Tenant Fix

**Date:** 2026-06-11 (2nd session) · **Commit:** `a9dd8a8f` (PUSHED origin+bitbucket, ahead=0)

## Session goal
`/security-scan` → `/ck-security` (STRIDE one-shot, then tenant-isolation Dapper sweep) → `/cook` fix the leaks found. Started as a routine security pass; the sweep surfaced 2 real cross-tenant read leaks.

## What worked
- **Scan/STRIDE = clean** (re-verified empirically, not from memory): parameterized Dapper everywhere, Vault-gated prod, 0 FE XSS, rate-limit broad, account lockout + escalation, file-upload magic-byte/zip-bomb, no SSRF (Ollama endpoint is config-bound), global exception handler guards prod.
- **BE NuGet verdict corrected**: prior memory said "slnx audit inconclusive (quirk)" — empirically the slnx audit *works*; the earlier empty runs were `EXIT=124` timeout + restore-lock from parallel `dotnet`. Verified per-host (`Api`+`AuthServer` `--include-transitive`) = CLEAN; per-module redundant (CPM 1-version-pin + hosts ref all 15 modules + 0 `PrivateAssets=all` + 0 other Exe host).
- **Tenant-isolation sweep found 2 leaks**, same root-class as the fixed UserDelegation bug (no TenantId column + no RLS + Dapper no-predicate): `ExternalIdentities` (List + GetById, sequential `long` Id = bulk-enumerable) and `LoginAudit` (the ONE of 4 audit controllers missing the `ResolveTenantId` pattern → unfiltered call dumps all tenants' login attempts).
- **Fix shipped**: query-level `EXISTS([identity].AspNetUsers tenant-bind) + @IsPrivileged`, no migration; GetById → NotFound-not-Forbidden. Build 0W/0E, code-review 0 logic defects. 48 tests green (28 unit + 10 Identity integ + 10 Audit integ, Testcontainers proving the cross-schema EXISTS against real SQL Server).

## What didn't / lessons
- **Tester subagent FALSE-PASS**: reported "18/20 passed, Build 0W/0E" when all 4 test files did not compile. Only `dotnet build`/`dotnet test` run by hand caught it. Never trust a subagent pass-claim — build+run yourself. (Reviewer + tester ran concurrently → reviewer's compile-error line numbers were a stale snapshot; the tester fixed compile after; current on-disk state had to be re-built for ground truth.)
- **`rg` is not in this box's Bash PATH** — `rg ... 2>/dev/null | wc -l` silently returns 0 (command-not-found hidden by the redirect). Every Bash-`rg` sweep was a fake-empty false-negative (same shape as the BE-NuGet timeout trap). Use the Grep tool; sanity-count input before trusting empty.
- **My own regression**: the query/handler signature change broke 2 pre-existing unit tests that live in **root `tests/`, not `src/`** — my caller-grep was scoped to `src/` and missed them. Grep callers across the whole repo. Fixed (preserve old intent with `IsPrivileged:true` + sub `IReadDbConnection`; added 2 new tenant-guard unit tests).
- **Privileged integration tests on a shared Testcontainer**: exact `HaveCount(2)`/`TotalCount==2` is contaminated by sibling tests' rows (privileged = no tenant filter → whole table). Product is correct; the assertion was wrong → switched to containment assertions + `PageSize:100`.
- **CWD trap**: an early `cd src/modules` persisted across Bash calls → later relative-path builds failed. Use `pwd` to confirm; prefer absolute paths.

## Decisions made
- **Query-level scope, no migration** (Model mirror of delegation fix) over adding a TenantId column + RLS — KISS, and login attempts/external-identities are child-of-user so binding via `AspNetUsers` is the natural boundary.
- **NotFound-not-Forbidden** on cross-tenant GetById — avoids leaking record existence under sequential-Id enumeration.
- **Privileged = SystemAdmin-only** for these two data types (stricter than delegation's SystemAdmin-OR-Admin) — external-identity + login-audit are more sensitive; a tenant Admin should not read other tenants'. Flagged for user confirmation (M1).

## Test status
- Unit (ExternalIdentities): 28/28. Identity integration: 10/10. Audit integration: 10/10. Build 0W/0E (4 production projects). code-review APPROVE (0 logic defects, red-team pass).

## Next session
- Filed follow-ups: ExternalIdentity **write-path** cross-tenant TAMPERING (Create/Update/Delete by arbitrary `long` Id — same controller, NOT fixed, read-only scope this slice); audit→identity cross-schema EXISTS tech-debt (move to `IIdentityModuleClient`); confirm privilege convention (SystemAdmin-only vs SystemAdmin-OR-Admin).
- Feature-dev phase still active (CI parked) — flagship feature decision still pending (lead candidate `T5-01` KPI Alerting).
