# T3-WF-CANDIDATEINDEX-READSIDE-EXTENSION Phase 01 + Phase 02 — 2026-05-25 08:58–10:45 ICT

**Type:** feature completion (readside extension — role-based candidate discovery)
**Component:** Identity.Infrastructure + Workflow.Application/Infrastructure
**Scope:** 3 commits (Phase 01 direct-only role names + Phase 02 predicate merge + docs)
**Cycle:** ~2h total (scout ~10min + impl ~45min + advisor ~45min + finalize ~30min)
**Tests:** Identity.Integration +5 (real-DB), Workflow.Application +2, Workflow.Integration +4 | 562→567 passing
**Code review:** 9.6/10 APPROVE (0C / 0H / 0M / 4L / 3Nit)

---

## What Shipped

**Phase 01 (`f4c5fc66`):** Identity contract `IIdentityModuleClient.GetUserRoleNamesAsync(userId, tenantId, ct)` returns `IReadOnlySet<string>` via Dapper SQL:
```sql
SELECT UPPER(LTRIM(RTRIM(r.Name)))
FROM [identity].AspNetUserRoles ar
INNER JOIN AspNetRoles r ON r.Id = ar.RoleId
WHERE ar.UserId = @UserId
  AND ar.IsActive = 1
  AND (r.TenantId = @TenantId OR r.TenantId IS NULL)
```
**PQ-1 A privilege-escalation defense:** Explicitly excludes group-inherited roles (UserGroupMembership→GroupRoleAssignment). Direct assignment only.

**Phase 02 (`f600c5a1`):** Repository predicates `IsPoolEligibleAsync` + `GetInboxAsync` gain `IReadOnlySet<string> roleNames` param. EF predicate now merges two streams:
- `PrincipalType=Permission ∩ permCodes` (existing)
- `PrincipalType=Role ∩ roleNames` (new)

`ClaimTaskCommandHandler` + `GetInboxQueryHandler` fetch role names from identity client at handler layer; pass to repo. 4 integration `StubIdentityClient` adapters extended; `WorkflowTasksController` xmldoc updated.

**Docs (`7cbaceb9`):** v2.137p110 changelog + dependency-map (new Phase 01 call-site documentation) + roadmap (Phase 03 B1 partial closure note) + code-standards + backlog sync.

---

## Test Coverage

| Layer | Delta | Notes |
|-------|-------|-------|
| Identity.Infrastructure unit | +4 facts | `GetUserRoleNamesAsync` mapping tests |
| **Identity.Integration** | **+5 real-DB** | **NEW `IdentityModuleClientRoleNamesTests`** — Testcontainers DB locks PQ-1 A SQL contract (direct-only, IsActive, tenant-filter); `_UserWithRoleViaGroupOnly_ExcludesRoleName` empirically locks the privilege-escalation defense |
| Workflow.Application unit | +2 facts | Handler propagation assertions (role names passed to repo) |
| Workflow.Infrastructure unit | 84/84 | Unchanged |
| **Workflow.Integration** | **+4 HTTP-driven** | **NEW `WorkflowTaskRoleClaimIntegrationTests`** — claim + inbox with stub identity; 3 pre-existing failures verified pre-existing via git stash baseline |
| Architecture | 105/105 | Unchanged |
| Build | 0E/0W | 7 pre-existing CS4014 warnings (unrelated) |

**Pre-existing test debt:** 3 failures in Workflow.Integration (`SimultaneousRace_*` flake + `I7_Activate_WithNotSupportedClauseMode_*` + `I_SaveAct_A4_PreviousHandlerClause_*`) filed as `T3-WF-RT2-15-TEST-DEBT-260525` (~1h, P2). NOT caused by this slice. Verified via `git stash` baseline run at HEAD `ca16d466`.

---

## Decisions (surface real reasoning)

### 1. Scout-first gate before cook (~10min, saved ~30min discovery)
`/aqt-next` recommended Option B (full slice) but user chose Option C (10-min scout first) to verify HTTP fixture precedent. Scout found `WorkflowInstancesListEndpointTests` as CLOSE precedent (HTTP + `ConfigureTestServices` stub pattern). Saved discovery-during-impl; execution path became clear before code started.

### 2. Pragmatic fixture simplification (mid-Phase 02)
Original plan envisioned seeding raw AspNet identity rows (UserGroupMembership/GroupRoleAssignment) for Workflow integration tests. Scout revealed precedent: stub `IIdentityModuleClient` via DI override was significantly simpler. **Decision:** Workflow integration kept 4 facts focused on predicate merge; cross-tenant + IsActive coverage delegated to Identity-layer real-DB tests. Trade-off: Workflow tests are tautological (prove stub-returns→predicate-uses) vs. contract tests (prove SQL emits what contract specifies). Tautological acceptable for consumer layer IFF producer layer has real-DB contract lock (✓ Phase 01 added that).

### 3. Advisor-driven tautology fix (~45min, prevented stale follow-up row)
After writing Workflow integration + before spawning code-reviewer, advisor flagged: "Stubbing `IIdentityModuleClient` then asserting 403 when stub returns empty doesn't lock PQ-1 A SQL contract." Original plan I2 ("Role via group → 403") would exercise real SQL. Advisor recommendation: add 5 real-DB Identity integration tests to lock the contract empirically. **Cost:** ~45min (write tests + iterate SQL schema fixes: `SEC_Group`→`UserGroups` rename / `UpdatedBy`→`ModifiedBy` audit-col / `ClassificationLevel` NOT NULL / Guid-suffix unique names vs RoleNameIndex). **Value:** Prevented filing another stale follow-up row (anti-pattern per memory `260522-A` — "carry-fwd lists drift"). Decision was lock-the-contract FIRST, consumer tests SECOND.

### 4. Pre-existing test-debt discovery at slice-end
Full Workflow.Integration baseline run at finalize surfaced 3 failures. Verified pre-existing via `git stash` + re-run at HEAD `ca16d466`. **Decision:** File as separate `T3-WF-RT2-15-TEST-DEBT-260525` (~1h, P2) — distinct from this slice. Memory note: prior entry `monday-260524-am-bp-fix-and-rt2-15-phase-01` claim "Tests all GREEN at every commit boundary" was inaccurate for Workflow.Integration (regression from Phase 01 or earlier).

---

## META-L Candidates (orchestrator decides promotion)

**260525-A — Anti-tautology test-layer placement:** Unit/mock tests cannot lock contracts whose surface IS the SQL/EF translation. Real-DB integration tests MUST reside in the owning module (here: Identity) when production SQL is the contract surface. Stub-based integration tests in consumer module (Workflow) are tautological — they prove predicate uses what stub returns, not that SQL emits it correctly. **Pattern:** split coverage by layer ownership. Contract layer = real-DB; consumer layer = stub-based.

**260525-B — Pre-existing test-debt discovery timing:** Running targeted baseline `dotnet test --filter <affected-module>.Integration` at slice-START (not just end) catches silent regressions inherited from upstream phases earlier. Extends memory `260522-A` re-grep-at-slice-start invariant to include baseline run. Cost ~5min; ROI high when inherited debt exists.

**260525-C — Cook `--auto` + advisor mid-slice intervention efficacy:** Advisor catch BEFORE code-reviewer spawn prevented ~1h stale follow-up row filing. Intervention value highest when crystallization not yet complete (here: pre-code-reviewer). Tool-based (cycle automation + advisor hook) beats process-based (PR review post-ship) for catching tautologies that _feel_ correct.

---

## Cycle Stats

| Phase | Task | Time | Notes |
|-------|------|------|-------|
| Pre-impl | Scout (HTTP fixture precedent) | 10min | Saved ~30min discovery cost |
| Impl | Phase 01 (Identity contract + SQL) | 15min | Pattern-mirror discipline (~5-7× faster than 2.5–3.5h plan estimate) |
| Impl | Phase 02 (Workflow predicates + handlers) | 25min | — |
| QA | Identity.Integration real-DB tests (advisor-driven) | 45min | SQL schema iteration + Testcontainers fixture |
| QA | Workflow integration + full baseline | 15min | Baseline catch 3 pre-existing failures |
| Finalize | Code-reviewer + docs parallel + git | 30min | 9.6/10 APPROVE |
| **Total** | — | **~2h (08:58–10:45)** | Wall-clock continuous |

## Invariant Closures

- **260523-D (fail-CLOSED-with-matching-reader):** Phase 03 B1 partial shipped write-side feeder Sun `ca16d466` (Role predicate producer); Mon AM adds matching reader. Cycle closes within 36h per invariant "same-PR-or-immediate-followup."
- **260524-G (privilege-escalation trap):** Direct-only roles must not reuse `IEffectivePermissionService` (includes group-inherited). Locked empirically by `_UserWithRoleViaGroupOnly_ExcludesRoleName` real-DB test.
- **260522-A (re-grep-at-slice-start):** Line numbers verified (`WorkflowTaskRepository.cs:120,202` from memory carry-fwd matched actual code Monday AM scout).

---

## Files Modified (in-scope)

- `src/Modules/Identity/Identity.Infrastructure/...`
- `src/Modules/Workflow/Workflow.Application/Features/...`
- `src/Modules/Workflow/Workflow.Infrastructure/Persistence/...`
- `docs/...` (changelog, dependency-map, roadmap, code-standards, backlog)

*Details in code-review report (9.6/10 APPROVE) — gitignored to reports dir.*

---

## Carry-forward

All 3 commits eligible auto-ship. Plan dir `plans/260524-2122-readside-extension-role-only/` gitignored (planner-deferred Sun EVE, full energy executed Mon AM per memory `sunday-eve-260524-rt2-15-phase-03-b1-partial`). Memory anchored: `260525-A`, `260525-B`, `260525-C` candidate promotion.
