# Journal — 2026-04-16 — T4-06 MasterData Cleanup + Cache Decision Framework

## Session Goal

Prepare codebase for Cases→Workflow refactor Phase 00 by addressing architectural debt discovered during planning audit:
1. Consolidate caching strategy (4 tiers chạy song song nhưng không có rule)
2. Clean up MasterData monolithic project (orphan dead code from 2026-03-31 refactor)

## What Worked (Verified)

### T4-06 MasterData Monolithic Deletion — SHIPPED (PR #24 merged, commit 25e70a6a)

- **Discovery → Plan → Execute** trong 1 session (~20 min)
- **Scope:** Xóa `src/modules/masterdata/AqtCoreFW.MasterData/` (41 files, -2226 lines); migrate 11 unit test files sang layered namespaces (Domain/Application/Infrastructure split)
- **Verification chất lượng cao:**
  - 56/56 unit tests pass Debug + Release (FQN diff 0 lines — test identity preserved qua migration)
  - Release build 0 warnings (TreatWarningsAsErrors enforced via `tests/Directory.Build.props:7`)
  - 10 grep sanity checks clean (no flat namespaces still referenced)
- **Dep-map corrected:** §MasterData từ "DbContext service-based, no CQRS" → documented CQRS split (Command+Validator in Application, Handlers in Infrastructure.CommandHandlers, Queries in Application using Dapper)
- **Red-team value proven:** RT-R2-01 critical finding — plan claimed "6 commands" nhưng thực tế chỉ 5. Nếu execute without Round 2 scout, Phase 01 Step 4 pre-flight sẽ fail (expect 6 folders, find 5) → investigator time sink

### Cache Decision Framework — SHIPPED (commit 96fbd731)

- Thêm 4-tier cache strategy vào `docs/code-standards.md` § "Cache Strategy Decision Framework"
- **4 tiers verified in codebase:**
  - T1: `IMemoryCache` (in-process per-pod, ~0.01ms) — Presence, ABAC rules, Rules Engine
  - T2: `IDistributedCache` (Redis only, ~2ms) — Menu, Claims, Permissions, Embeddings
  - T3: `ICacheService` / `TwoTierCacheService` (L1+L2, ~0.01ms L1 hit) — SystemParam, OrgUnit, Idempotency
  - T4: `ICacheService` + Redis pub/sub — feature flags (precedent via `SystemParamService:115` → `FeatureFlagSubscriber.cs`)
- Decision flowchart (security? best-effort? read frequency? cross-pod instant?) + TTL guidelines + invalidation patterns + existing usage map
- Deployment context documented: `infra/helm/aqtcorefw/values.yaml` → `replicaCount: 2`, HPA `maxReplicas: 10` → IMemoryCache-only creates stale window

### T4-05 Backlog Revised — SHIPPED (commit 92ac64ec)

- Original assumption "migrate all 3 to ICacheService, 1d" was WRONG
- Per-module strategy documented với concrete blockers:
  - **ABAC:** Valid Two-Tier migration (~3h)
  - **MasterData Dict:** DEAD CODE — consumer invalidates `masterdata:dict:*` but no query handler SETS this key. Delete both consumer + event (~30min)
  - **Rules:** CAN'T migrate — `RulesEngine.RulesEngine` has compiled delegates/LINQ expressions, NOT JSON-serializable → Redis serialization fails. Keep IMemoryCache + add Redis pub/sub (~3h)
- 3 unresolved questions captured for future executor decisions

### Cases→Workflow Refactor Plan — LOCAL (plans/260415-1737-cases-workflow-refactor/)

- 2 rounds red-team (Round 1: 12 findings; Round 2: 12 findings). All Critical/High resolved in-place.
- 7 files: plan.md + phase-00..06
- Unblocked by T4-06 completion (clean MasterData landscape)

## What Didn't / Lessons

### RT-R2-01 revealed fundamental scout gap

Plan original claimed "6 commands" without verification. Real count is 5. Lesson:
- **Any plan with numeric counts needs empirical scout verification** — never trust assumed numbers
- **Red-team Round 2 value > Round 1** in this case — second round caught factual error first round missed
- Pattern: run `ls` on target dirs BEFORE writing plan, embed evidence

### RT-R2-05 ROI surprise (unnecessary worry)

Worried về DbContext constructor drift (monolithic vs layered) — thực tế identical signature `(DbContextOptions<MasterDataDbContext>, ITenantContext)`. 5 phút scout giải quyết 1 whole risk category.

### Dirty-tree gate reconsideration

Plan Round 1 insisted "STOP on dirty tree". Reality: tất cả git operations trong plan đều scoped (no `git add -A`), nên dirty files không thể leak. Round 2 fix: pre-flight check giữ as defensive documentation, không strict blocker.

### CI pipeline broken pre-session

GitHub Actions CI failing on `main` since 2026-04-12 (3 runs in history, all red). Not T4-06 regression. PR #24 merge không có CI gate — local verification (Debug+Release builds + 56/56 tests + 10 greps) trở thành de-facto quality check. Note for later: investigate CI workflow failures.

## Decisions Made

1. **Cache tier selection framework:** codified 4-tier decision into `docs/code-standards.md`. Future cache decisions must reference the decision flowchart.
2. **MasterData layered structure locked:** monolithic project deleted. Future MasterData work (including Phase 00 taxonomy) uses Domain/Application/Infrastructure/Presentation only.
3. **PR #24 squash-merge:** repo has no branch protection; historical CI been failing → per-phase commit granularity lost post-merge (rollback = `git revert HEAD` on squashed commit, not per-phase hashes). Accepted trade-off for clean main log.
4. **T4-05 defer:** not blocking Phase 00 (no entity/schema conflict). Revised plan awaits future session.

## Test Status

| Suite | Pre-T4-06 | Post-T4-06 | Delta |
|-------|-----------|------------|-------|
| AqtCoreFW.MasterData.Tests (unit) | 56/56 pass | 56/56 pass | 0 FQN diff |
| Debug build | 0 warn/0 err | 0 warn/0 err | unchanged |
| Release build (warnings-as-errors) | 0 warn/0 err | 0 warn/0 err | unchanged |
| Integration.Tests | requires Testcontainers (skipped locally) | requires Testcontainers | CI will verify |

## Next Session

1. **Cases→Workflow Phase 00 start** — Workflow Masterdata Taxonomy (~54h foundation). Plan: `plans/260415-1737-cases-workflow-refactor/phase-00-workflow-masterdata-taxonomy.md`. Chia thành 3-4 sub-sessions vì scope lớn.
2. **T3-02 Shared SQL Server** (0.5d) — quick win nếu muốn break up large phase
3. **CI pipeline recovery** — GitHub Actions failing since 2026-04-12, unblock quality gates before more features ship
4. **T4-05 execution** — khi có bandwidth, per revised per-module strategy

## Related Artifacts

- **PR #24:** https://github.com/thiennd212/AqtCoreFW/pull/24 (merged)
- **Plan T4-06:** `plans/260416-1036-t4-06-delete-masterdata-monolithic/` (3 phase files + Round 1+2 red-team notes)
- **Plan Cases→Workflow:** `plans/260415-1737-cases-workflow-refactor/` (local, unblocked)
- **Cache framework:** `docs/code-standards.md` § "Cache Strategy Decision Framework" (lines ~485-570)
- **Backlog T4-05 revised:** `backlogs/backlog.md` entry (per-module strategy)
