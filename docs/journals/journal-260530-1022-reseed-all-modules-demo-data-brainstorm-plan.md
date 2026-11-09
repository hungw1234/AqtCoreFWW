# Journal — Reseed All Modules Demo Data: brainstorm → plan → validate

**Date:** 2026-05-30 ~10:22
**Type:** Planning session (NO code change). Brainstorm + plan(7-phase) + validate.
**Artifacts:** `plans/260530-0803-reseed-all-modules-demo-data/` (brainstorm-summary, module-by-module-seeding-catalog, plan.md + phase-01..07, Validation Log).

## What

`/aqt-runapp` (dev stack up — Auth 5002 / API 5001 / FE 5173, Vault re-seeded in-memory) → `/brainstorm` "reseed toàn bộ data tất cả module" → 17-module per-module brainstorm → `/ck:plan` 7-phase → `/ck:plan validate`. Stopped before cook (user will /clear + cook P1).

## Key decisions (user-driven, anti-auto-default)

- **Mechanism**: nuke DB (volume `down -v` OR `DROP DATABASE`) → migrate → seed. Dev-only hard guard.
- **Philosophy lock**: seed **DEFINITIONS ONLY** — NO instances/runs/submissions (user sinh runtime bằng chạy BP thủ công). → killed the in-process-vs-HTTP "operational data" fork entirely; all seeds = in-process factory (E2ETestDataSeeder pattern).
- **2 tenants**: Bộ Tài chính (BTC, sẵn ~1139 đơn vị/10k users) + Bộ Nội Vụ (NEW ~100-200 đơn vị thật/~1-2k users). Tenant names có dấu.
- **Seed**: Tenant rows ×2, MasterData (3-city địa giới HN/HCM/ĐN + **bỏ CaseTypes** + JobTitles per bộ), roles BTC_*/BNV_*, Workflow defs + assignment rules, Form templates, BP defs (Draft), Rules/Reporting/Integration defs.
- **Skip (explicit, not omission)**: Collaboration/Ai/Files/Audit (runtime/config/activity), permissions mappings, all runtime data.
- **Enabled default**: demo-seeders OFF in normal Dev, reset-all sets env to enable (avoid startup bloat + integration-test phình).

## Verified facts (scout, not assumed)

- All ~12 seeders idempotent `IHostedService, IIntegrationTestSeeder`; `BtcIdentitySeeder` has drift-WIPE (not pure skip) — counts BTC users, wipes dependents + reseeds, 50k sanity ceiling.
- **Tenant table seeds ZERO rows** today (only `TenantId` stamped on users/org) → "2 bộ" needs new `TenantSeeder`.
- BTC tenant name `"Bo Tai Chinh"` = ASCII (no diacritics) → fix to có dấu, keep code+GUID.
- **CaseType ≠ Cases module**: Cases module deleted but CaseType is standalone MasterData dict (`GetCaseTypesQueryHandler` live) — yet FE consumes only orval generated client (no real component) → near-vestigial → drop from seed (user call).
- **Org tree = real Bộ Tài chính** (Thuế/Hải quan/Kho bạc, 1139 units); E2E seeds separate "Bộ Nội Vụ" 4-unit tree referenced by 3 E2E specs (`org-units-write`/`org-staff-assignments`/`crud-org-units`) → **do NOT unify** (would break specs); fold 4 pinned GUIDs into real BNV tree instead.
- Seeders hard-code `Btc*Constants` → P2 must **generalize tenant-agnostic** (profile config) to support 2nd ministry. Reg sites: `InfrastructureRegistration.cs:344` (BtcIdentity), `OrgRegistration.cs:64` (BtcAssignment).

## Brutal-honesty surfaced

- "BNV ~1000 đơn vị tương đương BTC" rejected — real Bộ Nội Vụ much smaller (no regional chi-cục network) → user revised to ~100-200 real. 2 bộ NOT scale-equal.
- Scope reality: multi-week. Heaviest = generalize Btc*Seeder + Tenant seeder + **content sourcing** (BNV structure + phường hệ-34) + 4 new module seeders (BP/Rules/Reporting/Integration have none).

## Validation outcome

Full tier, 7 phase. Claims checked ~10, **Failed: 0**. Whole-plan consistency sweep: 0 contradictions → cook-eligible. 2 content-sourcing blockers (BNV org, phường hệ-34 for HN/HCM/ĐN) converted to explicit P2/P3 sub-tasks; user chose self-research from public sources.

## Process meta (lessons)

- **User repeatedly rejected bundled auto-recommendations** ("không chấp nhận đề xuất tự động") — wanted genuine per-module co-determination, not rubber-stamp. Pivoted from "recommend + confirm-in-bulk" to one-module-at-a-time solution-space + user decides. Advisor flagged the same: stop multiSelect rounds, deliver analysis + "đề xuất X — nói nếu không đồng ý".
- Scout-first paid off: verified CaseType-live-but-UI-dead, Tenant-rows-absent, org-tree-dual, E2E-GUID-refs empirically before recommending — each corrected a naive assumption.
- Advisor's biggest catch (HTTP-driven seed via ROPC vs in-process MediatR auth-context fragility) became MOOT when user cut runtime-data scope — but was the right call to surface; documented as resolved-by-scope-cut.

## Unresolved questions (carry to cook)

- BNV real org structure source — user self-research; accuracy risk until sourced.
- Phường hệ-34 data for HN/HCM/ĐN (QĐ19/2025) — sourcing pending.
- Exact env var name to gate demo-seeders on reset (`Seed__Demo__Enabled` vs per-seeder) — decide in P1/P2 impl.
- DRY: JobTitles vs `BtcPositions` (15 entries) — share source or duplicate (decide P3).
