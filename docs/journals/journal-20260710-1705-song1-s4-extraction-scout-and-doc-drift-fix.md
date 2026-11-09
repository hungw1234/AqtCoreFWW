# Journal — Sóng-1/S4 Extraction-Readiness Scout + Stale Doc-Drift Fix

**Date:** 2026-07-10 ~17:05 ICT · **Branch:** main · **Commit:** `e803a272` (1 ship) · **Session type:** scout + docs-hygiene (late-day, post-11-ship)

## Session goal
`/aqt-next` orient at session start → honor standing #1 priority (microservices transformation). Fatigue heuristic fired (11 prior ships + past 15:00), so instead of opening a large `/ck:plan` Sóng-1 late-day, ran the **read-only scout** that is the natural Phase-0 of that plan, then shipped the cheap doc-drifts it surfaced.

## What worked (verified)
- **2 parallel Explore scouts** (read-only) mapped Sóng-1 = Extract S4 (Docs/Sign/AI) cleanly:
  - **S4 boundary is small & clean:** `files` (1 outbound→S1) + `ai` (**0 client coupling**, DEFERRED impl) + `reporting` (2 outbound→S1+S2). Only **7 cross-service edges** total. Confirms roadmap's "fewest chain constraints → first split" rationale empirically.
  - **`search` = empty scaffold:** 0 hand-written `.cs`, 0 `.csproj`, absent from build (only stale `obj/` artifacts). Unmappable on code evidence; sole signal (deferred `/ai/search`+`/copilot` FE routes) → S4/AI, not S3/S5.
  - **Gap #3 (gRPC auth-ctx):** no gRPC exists yet (all 18 clients `InProcess*`). Sóng-0 already solved the async-consume half (`AmbientTenantScope`+`TenantScopeConsumeFilter` pin tenant→RLS, proven `ConsumeScopeTenantContextTests`). Residual = sync gRPC interceptor-pair + authenticated claim propagation + authz-claim carriage (`AmbientTenantScope` carries TenantId only → couples authz gap #6).
  - **Gap #10 (test substrate):** `WebAppFixture` seeder false-green fixed, but bus/outbox/consumer still stripped → consume/outbox-delivery regressions stay green. Needs multi-host + real broker + fault-injection + non-db_owner login.
- **Ship `e803a272`** — 2 stale doc-drifts corrected + pushed origin/main (0/0 verified):
  - dependency-map: removed stale `Reporting` from `IOrgUnitModuleClient` consumers at **both** table sites.
  - roadmap §2 + backlog: reframed `search` from "S5/S3 unresolved" → "empty scaffold, S4/AI signal, product-gate".

## What didn't / lessons
- **Drift lived in 2 places; the scout report only caught 1.** The scout flagged `dependency-map.md:58` (client table). A post-edit `grep OrgUnit` sweep caught a **second** stale `Reporting` at L99 (shared-projects table). Trusting the scout's report verbatim would have left L99 stale. → **INVARIANT 260522-A dividend: verify-empirically (grep the whole file) even after a scout hands you a specific line.** A symbol/consumer removal must grep repo-wide, not just the one cited site (twin of `feedback-symbol-removal-grep-must-span-tests`).
- **Scout report had a minor internal contradiction** (claimed "no .csproj" while `obj/` build artifacts existed). Resolved by direct glob: 0 `.csproj` confirmed → obj/ are stale leftovers. Lesson: reconcile a scout's contradictory evidence with a direct probe before writing it into an SSOT.

## Decisions made
- **Scout-first over plan-now** (fatigue trade-off): same #1-track advance, read-only ~30min vs 1–2d plan authoring on a 12-ship day. Scout output = the input `/ck:plan` Sóng-1 needs.
- **`search` left unmapped behind a product gate**, provisionally S4/AI — not force-assigned S3/S5. Extracting an empty scaffold is a no-op → zero migration cost → does not block Sóng-1.
- **No separate backlog rows filed for gaps #3/#10** — already tracked in roadmap §8 + `MSVC-WAVE0-DEFER-TRACK`; `/ck:plan` Sóng-1 will formalize them as phases P1–P3 (avoid duplicate-row noise).
- **Backlog optimize skipped** — only 1/3 triggers (BL_LINES=648, 8% over; CLOSED_HDRS=5, HDR=14 compact). Full awk-restructure late-day = high-risk-low-reward churn.

## Test status
N/A — docs-only ship (0 code, 0 test). Build gates not required. Working tree clean, 0/0 origin/main.

## Next session
1. **`/ck:plan` Sóng-1 (S4 Docs/Sign/AI extraction)** — author multi-phase plan from the scout report §6 skeleton (P0 boundary+storage-adapter seam → P1 gRPC tenant-propagation interceptor-pair → P2 authz-claim carriage → P3 distributed-test substrate → P4 physical S4 host + logical-DB split). P1–P3 are reusable for every later wave.
2. **3 product/gate Qs to resolve before P4** (scout report §Unresolved): `search` defer-vs-provisional-S4; Sóng-1 extraction date (`MSVC-WAVE0-DEFER-TRACK` Open-Q #5); gRPC hosting model (Kestrel-HttpContext vs pure-gRPC) — decide before P1.
3. Optional: backlog optimize when it crosses more triggers (currently marginal).
