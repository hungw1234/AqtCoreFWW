# Module-RLS Guard — Exhaustive Discovery + TenantSignatureConfig Allowlist (2026-06-24)

**Slice:** `/aqt-next` → `/cook` RLS-bundle (2 backlog rows T3). Test-only. Commits `10e1f530` (test) + `c72b1f04` (docs), pushed origin + bitbucket.

## Session goal
Close `ARCH-MODULE-RLS-GUARD-DISCOVERY-EXHAUSTIVE` + `RLS-TENANTSIGNATURECONFIG`: make the module-RLS tenant-isolation guard discover entities that have no `DbSet` (mapped only via `ApplyConfigurationsFromAssembly`), and resolve `TenantSignatureConfig`'s missing RLS coverage. Chosen over cooking signature P2 (a larger, multi-session phase) as a contained warm-context slice.

## What worked
- **Model-based discovery, test-only.** Replaced the `DbSet<T>` property walk in `ModuleDbContextTenantIsolationGuardTests` with reflective instantiation of each of the 13 module DbContexts (stub `ITenantContext`/`IKeyResolver` + a bogus SQL Server connection string — no DB opened) reading `Model.GetEntityTypes()` + `GetTableName()`. Covers `TenantSignatureConfig` and uses real relational table names; deleted the `Pluralize()` heuristic + override map.
- **TenantSignatureConfig → predicate-only allowlist** (not RLS). Documented reason: `SigningHealthCheckService` (startup `IHostedService`) counts enabled configs across all tenants with no ambient tenant → RLS under least-privilege would zero-row the check (ADR-009 class); `IdentityDbContext` applies no tenant query filter; request-scoped readers isolate via explicit `WHERE TenantId`.
- **Fixed a pre-existing false-positive in `IsRlsCovered`.** The fragment `_{table}_Tenant` is a substring of the conventional unique index `UQ_{table}_TenantId`, so a table with that index looked RLS-covered when it wasn't. Tightened to the exact RlsMigrationHelper artifacts `_{table}_TenantFilter` / `_{table}_Tenant]`.
- Full arch suite **149/149**; code-reviewer **APPROVE** (3 nits, no change).

## What didn't / lessons
- **Initial scout (Explore) over-pessimized Design A.** It reported that `Model.GetEntityTypes()` needed either a production refactor of the shared `ApplyGlobalFilters` or a heavy DI fixture (12/15 contexts require `ITenantContext` in their ctor → NRE on options-only construction). Reading `ModelBuilderExtensions:25` directly showed `Expression.Constant(tenantContext)` reads `.TenantId` **per-query**, so the model builds fine with a stub context. A test-side stub achieves the chosen Model-based outcome with zero production risk. **Lesson: when a scout says "you must change prod to test X", read the mechanism before accepting the scope — a captured-service expression evaluated per-query means a stub is enough.**
- **The teeth-proof exposed a decorative allowlist.** Removing the allowlist entry was expected to flag the entity, but the test still passed — because `IsRlsCovered` false-matched the `_TenantId` index. **Lesson: a teeth-test that passes when you expect it to fail is a signal of a false-positive in the detection layer, not "already correct."**
- **The backlog row's RLS-vs-allowlist lean was a guess, not a probe.** The row tentatively said "RLS fits"; the empirical reader-hunt found a cross-tenant startup reader → allowlist. Same class as the SIGN-PENDINGTX least-privilege trap.

## Decisions made
- **Design A (Model.GetEntityTypes) over Design B (config-class reflection)** — user choice; kept entirely test-side via stub context rather than refactoring production. Trade-off accepted: more code + a broader table-name path change (all ~30 entities now use `GetTableName()`), validated by the full 149/149 suite showing no regression.
- **Allowlist, not RLS, for TenantSignatureConfig** — overturned the backlog's tentative RLS lean on empirical evidence.
- **Did not auto-compact MEMORY.md** (index now ~350KB, over the load limit) — flagged as a separate housekeeping pass; too risky/heavy to fold into this slice.

## Test status
- Architecture suite: 149/149 pass, 0 skipped, 0 warnings.
- Production code changed: 0. Migrations: 0.

## Next session
- **Signature P2** (multi-provider self-service enrollment, plan `260623-1950`) — scout completed this session: READY, 0 design blocker. Carry two stale-claim corrections: H5 guard already shipped in P1 (`UpdateUserCommandHandler:39-53` → P2 only tests it); `SignerEnrollmentNonce` is net-new in P2 (plan.md dependency line wrongly lists it as P1-delivered).
- **MEMORY.md compaction** — index over the 24KB load limit; needs a dedicated slim pass.
