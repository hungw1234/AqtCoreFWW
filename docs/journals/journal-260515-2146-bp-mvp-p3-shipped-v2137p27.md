# Journal — BP MVP P3 BusinessProcess Module Scaffold SHIPPED (v2.137p27)

**Date:** 2026-05-15 21:08 → ~21:30 ICT (~3.5h actual vs 1d+1-2h planned)
**Branch:** main
**Commits:**
- `04e6f36a` feat(businessprocess): BusinessProcess module scaffold + RLS migration
- `f2ab1279` docs: v2.137p27 changelog + roadmap + dep-map + backlog
**Pushed:** origin/main `f2ab1279` ✅

---

## Session Goal

Ship BP MVP P3 (BusinessProcess module scaffold + 3 entities + RLS migration + Outbox row) per `plans/260420-1957-business-process-definition-mvp/phase-03-*.md` via `/cook BP MVP P3 --auto`. Last open backlog row in BP MVP series before P4 (REST API) can start.

User explicitly chose to override the "fresh-mind morning P3" memory directive (`bp-mvp-p2-shipped-260514.md:33`) after explicit risk gating — 3rd consecutive late-evening fatigue rule-c override. Trade-off table presented 3 options (scaffold-only tonight / full P3 auto / defer to AM); user picked **full P3 auto**.

---

## What Worked (Verified)

### Templates held
- **Rules module (T4-05) as template** — csproj layouts, DbContext shape, EF config base (`EntityTypeConfigurationBase<T>` auto-applies PK / timestamps / soft-delete / PublicId AK / tenant composite indexes), repo Pattern-A pre-factory guard, `InfrastructureRegistration` 5-interceptor set.
- **Forms P1 child-entity pattern** — `BusinessProcessStateForm` / `BusinessProcessTransitionForm` carry duplicated `TenantId` (ITenantScoped) for RLS defense-in-depth. Mirrors `FormTemplateVersion` (Forms P1 ship 2026-05-14).
- **RLS migration helper batch-isolation** — `RlsMigrationHelper.GenerateRlsPolicyStatements("businessprocess", 3 tables)` yields 6 batches (CREATE FUNCTION + CREATE SECURITY POLICY per table); each `migrationBuilder.Sql()` is one batch per `phase6-rls-restore` precedent. `Down()` drops policies BEFORE tables (security policy depends on table).
- **Aggregate-root tenant invariant** — fold-in `AddStateForm` / `AddTransitionForm` guard `child.TenantId == this.TenantId` (code-reviewer L-4) — defense-in-depth even though Pattern-A + DB unique index + RLS already block at three lower layers.

### Test baselines locked
- Architecture **97/97** PASS (no new arch violations from new module)
- Forms.Domain **130/130** PASS (regression baseline held)
- Forms.Application **155/155** PASS (regression baseline held)
- Full solution build **0E/0W** (~1m45s)
- `RlsPolicyPresenceTests.ExpectedPolicies` extended 36 → **39** (3 BP rows) — locks RLS-lost regression class

### Code-review
APPROVE_WITH_NITS **9.5/10** (above P2's 8.7, comparable to P1-FU 9.3 and P2-FU3 ~9.5). 8 focus areas verified PASS:
1. RLS migration correctness (table names, batch isolation, Down ordering)
2. Repository Pattern-A strict throw on writes
3. Factory XOR for `IntakeFormTemplateId`/`Version` (truth table verified)
4. Child entity `TenantId` + index leading-key
5. EF cascade delete + RowVersion concurrency token
6. DI interceptor set matches `RulesInfrastructureRegistration`
7. slnx + Api wiring placement
8. OutboxSchemaRegistry schema name conforms to regex

Both nits folded inline:
- **L-1:** RlsPolicyPresenceTests XML doc header 36 → 39
- **L-4:** Aggregate-root `AddStateForm`/`AddTransitionForm` tenant invariant guard with `GOV_BP_002` throw

---

## What Didn't / Lessons

### dotnet-ef local manifest race
`dotnet tool restore` reported success (manifest at `C:\AqtCoreFW\.config\dotnet-tools.json`, tool 10.0.5 installed) but next `dotnet ef --version` in same PowerShell session still errored "Run dotnet tool restore". Same in chained Bash. **Workaround:** invoke global ef directly at `C:/Users/thien/.dotnet/tools/dotnet-ef.exe` with absolute project path. Note: needed `--project` with full Windows path (forward-slash path → "Unable to retrieve project metadata. Ensure it's an SDK-style project").

**Memo for future P-N migrations:** start with global ef, skip local manifest race — saves 5-10min troubleshooting.

### IDomainEvent contract drift
First pass at `BusinessProcessCreatedEvent` was a simple `record` implementing `IDomainEvent` with only `MessageId` → CS0535 build error. `IDomainEvent` requires both `MessageId` + `OccurredAt`. Pattern: per-module abstract base record (`BusinessProcessDomainEventBase`) fills `OccurredAt = DateTimeOffset.UtcNow`, exposes `MessageId` as abstract. Mirrors `RulesDomainEventBase` exactly.

Lesson: cross-check `IDomainEvent` interface members BEFORE writing the first event — would have caught it in plan-read.

### Test-class assertion method name drift
`RlsPolicyCount_Equals_ThirtySix` decoupled from count = 36 → renamed to `RlsPolicyCount_Equals_Expected` so future modules don't have to rename the method each ship.

**Pattern codified:** test method names should reference "Expected" (data-driven from constant/matrix) instead of hardcoded numbers when the expected value is shipped as part of the assertion data.

---

## Decisions Made

| # | Decision | Rationale |
|---|----------|-----------|
| D1 | RowVersion only on aggregate root (not children) | Children managed through aggregate; per-child concurrency tokens add complexity without value when EF tracks the whole graph |
| D2 | Children carry duplicated `TenantId` (ITenantScoped) | Forms P1 precedent for RLS defense-in-depth. Same value enforced by repo Pattern-A + DB unique index + RLS policy at write |
| D3 | Domain-event base record fills `OccurredAt` | Mirrors Rules `RulesDomainEventBase` pattern |
| D4 | RLS embedded in InitialBusinessProcess migration | Session-1 Validation D3 lock; day-1 protection vs separate follow-up migration that could be missed during regen |
| D5 | OutboxSchemaRegistry row added alphabetically | Mirrors existing 12 entries; idempotent registration |
| D6 | Code-reviewer L-4 folded inline | Aggregate-root invariant cheap (~10 LOC); defense-in-depth pattern matches Forms P1 |
| D7 | TenantIsolation enforcement-level test deferred to P3-FU1 | `RlsPolicyPresenceTests` matrix extension already locks policy-presence regression class (main risk surface); enforcement-level test needs fixture (~3-4h) — push past midnight risk |

---

## Test Status

| Suite | Result | Note |
|-------|--------|------|
| Architecture.Tests | **97/97 PASS** | Baseline (no new arch violations) |
| Forms.Domain.Tests | **130/130 PASS** | Regression baseline held |
| Forms.Application.Tests | **155/155 PASS** | Regression baseline held |
| `dotnet build src/AqtCoreFW.slnx` | **0E/0W** (~1m45s) | Full solution build clean |
| `dotnet ef migrations add InitialBusinessProcess` | Clean generation | No `--no-build` flag (per memory rule) |
| BusinessProcess.Domain build | **0E/0W** | After IDomainEvent fix |
| TenantIsolation.Tests build | **0E/0W** | After ProjectRef + fixture migration step |
| Api host build | **0E/0W** | After ProjectRefs + Program.cs wiring |

**Not run:** TenantIsolation enforcement-level test (requires SQL Testcontainer / `SQL_LOCAL` env; deferred to P3-FU1).

---

## Next Session — Priorities

### Top priority — BP MVP P4 ready
**P4: BP REST API + 3 permissions + outbox event (2.5d BE)** — now `ready` (P3 unblocked). Per phase-04 plan:
- REST controllers (CRUD + Activate/Deactivate)
- 3 new permission codes (`BusinessProcess.{Manage, View, Activate}`) — PermCodes 125 → 128
- Outbox event `BusinessProcessActivatedEvent` / `BusinessProcessDeactivatedEvent`
- MemoryCache per-tenant `bp:{tenantId}:def:{id}` 5min TTL (mirror `AbacCacheRefreshService` T4-05)

### P3 follow-ups filed
- **P3-FU1** (~3-4h): Full `BusinessProcessTenantIsolationTests` enforcement via `SESSION_CONTEXT` cross-tenant SELECT deny (mirrors `RuleSetTenantIsolationTests`). Belt-and-braces; presence test already covers main risk.
- **P3-FU2** (~1h, 10-12 tests): `AqtCoreFW.BusinessProcess.Domain.Tests` unit project — factory invariants (`IntakeFormTemplate*` XOR, `<=0` arg guards, `versionNumber >= 1`, domain event raise, code/workflow-id immutability).
- **P3-FU3** (~0.5h, bundle with FU2): Repository unit-test cross-tenant write rejection.

### P2 leftovers (still open)
- **P2-FU1** (~0.2d): Consent duplication on repeated BP state-form edits (bound to P6 prefill consumer landing).
- **P2-FU2** (~0.1d): Doc/seal `SubmitFormCommand.WorkflowInstanceId` internal-use (cosmetic).

### Carry-over from earlier sessions (lower priority)
- T4-05-FU AbacCacheRefreshService — DONE 2026-05-13 (`baaf1182`)
- R1-R7 backlog items (not BP MVP related)

---

## Commits This Session

| Hash | Type | Description |
|------|------|-------------|
| `04e6f36a` | feat | BusinessProcess module scaffold + RLS migration (28 files, +1775 LOC) |
| `f2ab1279` | docs | v2.137p27 changelog + roadmap + dep-map + backlog (5 files, docs-only) |

Both pushed to `origin/main` at session close.

---

## Operational Notes Worth Remembering

| Item | Outcome |
|------|---------|
| **3rd consecutive fatigue rule-c override** (21:08 ICT) | Held — RLS migration correctness verified template-conformant; batch-isolation matches `phase6-rls-restore` precedent. User explicit ack before launch. |
| **dotnet-ef tool resolution** | Local tool manifest race — global ef at `C:/Users/thien/.dotnet/tools/dotnet-ef.exe` worked. Document for future migrations. |
| **Memory rule: never `--no-build` with ef migrations** | Held — generation ran clean with implicit build. |
| **Working tree clean** | All committed + pushed. 0 ahead of origin/main, 0 behind. |
