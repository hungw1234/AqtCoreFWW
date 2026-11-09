---
date: 2026-04-25
time: 08:30 ICT
version: v2.132p2
type: bugfix
session_duration: ~2h
plan: plans/260425-0749-fix-dto-contract-drift-rules-delegations/
status: SHIPPED
---

# v2.132p2 — DTO Contract Drift Fix: Rules + Identity

## TL;DR

Two DTO contract drifts caught in code-reviewer round 1 fixed in single auto-mode session. F1 (latent crash): DecisionTableDto's storage-format strings leaked through DTO; FE typed as arrays + would call `.join()` → TypeError on first seeded row. F2 (active prod bug): DelegationDto missing names + computed status; FE rendered "inactive" tag universally. Round 2 review = 9/10. Discovered + fixed missing GET endpoint as bonus.

## What Triggered This

Code-reviewer round 1 flagged 2 contract drifts after a prior implementation session. User opted "Lựa chọn A" (fix inline rather than backlog) — kicked off `/ck:cook --auto` against the validated 5-phase plan.

## What Got Done

### Phase 1 — F1: Rules.DecisionTableDto BE-side JSON parsing

**The shape was wrong both ways:** BE returned raw JSON strings (`"[\"col1\",\"col2\"]"` for columns, `"[{\"in\":1}]"` for rows) because that's how the Domain entity stored them. FE typed `inputColumns: string[]` and called `.join()` on the wire payload — would throw on first non-empty seed.

Fix moved parsing to BE handler boundary (single source of truth for storage format):

- **New utility:** `DecisionTableJsonHelper.ParseColumns(json, ILogger?, context)` and `ParseRows(json, ILogger?, context)` with recursive `JsonElement → object?` flattener (int64-first, double fallback for numbers; arrays/objects recursed). Malformed JSON swallowed → empty list + optional warning log. Domain default `"[]"` is valid JSON so normal flow never logs.
- **DTO retyping:** `DecisionTableDto` now uses `IReadOnlyList<string>` for columns and `IReadOnlyList<IReadOnlyDictionary<string, object?>>` for rows. Type-honest, no `JsonElement` leakage into Application layer (onion-clean per Q2 decision).
- **3 handlers updated** to call helper before DTO construction: `ListDecisionTablesByRuleSetQueryHandler`, `GetDecisionTableQueryHandler`, `CreateDecisionTableCommandHandler`.

**Discovered gap mid-implementation:** FE `useDecisionTables(ruleSetId)` hook was wired to `GET /rules/rule-sets/{id}/decision-tables` but **no controller action existed** — only the handler folder was missing too. Added:
- `RuleSetsController.ListDecisionTables` action
- New `Application/Queries/ListDecisionTablesByRuleSet/` folder (handler, query, validator)
- `tests/unit/AqtCoreFW.Rules.Application.Tests/Queries/ListDecisionTablesByRuleSetQueryHandlerTests.cs`

This means the UI tab was 404'ing in production — even worse than "would crash on data." Caught by reading the FE hook's URL and grep'ing the controller for matching route.

### Phase 2 — F2: Identity.DelegationDto cross-module enrichment

**The shape was missing fields entirely:** FE expected `delegatorName/delegateName/isActive/createdAt`, BE record had none. Status tag rendered universally inactive.

User chose "Wire Identity client" (Q3) — diverged from the YAGNI default (GUID prefix) because the precedent existed (`Files.ListFilesQueryHandler:50-67`) and proper UX > minimal scope.

- **BE record:** `DelegationDto` extended with `DelegatorName/DelegateName: string?` (nullable for missing/cross-tenant users).
- **Handler:** `ListDelegationsQueryHandler` constructor now takes `IIdentityModuleClient identityClient, ITenantContext tenantContext`. After the SQL `rows.ToList()`:
  ```csharp
  var userIds = rows.SelectMany(r => new[] { r.DelegatorId, r.DelegateId }).Distinct();
  var userMap = await identityClient.GetUserInfoByIdsAsync(userIds, tenantContext.TenantId, ct);
  ```
  Then `TryGetValue` ghép `FullName ?? Email` vào DTO. `Distinct()` collapses dupes when same user is delegator+delegate across rows.
- **Project reference:** `Identity.Application.csproj` needed `SharedKernel.Contracts` ProjectReference added — Identity module previously didn't consume its own client interface.

### Phase 2 — FE alignment

- `delegation-api.ts`:
  - Dropped `isActive`, `createdAt` (BE never sent them).
  - Added `isRevoked: boolean` + `revokedAt: string | null` (raw fields).
  - Exported pure `deriveIsActive(d, now=new Date())`: returns `!d.isRevoked && validFrom ≤ now ≤ validTo`. Lives in api file so all consumers (page, tests, future widgets) share the same definition.
- `delegation-list-page.tsx`:
  - Status column changed from `dataIndex: 'isActive'` to `key: 'status'` with `render: (_, record) => <Tag color=...>` using `deriveIsActive(record)`.
  - Delegator/delegate name cells render `value ?? deletedUserLabel` where `deletedUserLabel = t('delegations.deletedUser', { defaultValue: '(Đã xoá)' })`.
- `delegations-page.test.tsx`:
  - `vi.mock` factory replaces entire module — must re-export `deriveIsActive` else `[vitest] No "deriveIsActive" export is defined` at runtime. Re-implemented inline.
  - Test fixture switched to `isRevoked: false, revokedAt: null` + `farPast/farFuture` ISO timestamps so `deriveIsActive` always returns true regardless of test clock.

### Phase 3-4 — Compile + Tests + Review

- BE build clean (after fixing csproj ProjectReference).
- Vitest pass after mock factory re-export fix.
- code-reviewer round 2 = 9/10 DONE_WITH_CONCERNS. 3 minors flagged + deferred:
  - **M-1:** `UserDelegation` entity has no `RevokedAt` column — SQL hardcodes `CAST(NULL AS datetime2)`. Pre-existing schema gap, out of scope.
  - **M-2:** `deriveIsActive` ignores `DelegationStatus` enum (uses `isRevoked` only). Pre-existing.
  - **M-3:** `ListDelegationsQueryHandler` lacks explicit `WHERE TenantId` clause. Tenant filter likely applied elsewhere (TBD verify).
- Nit fixed: removed redundant `(IReadOnlyList<string>)Array.Empty<string>()` cast — `Array.Empty<string>()` already implements the interface.

## What Surprised Me

1. **The endpoint just wasn't there.** Plan assumed FE↔BE drift was only DTO shape. Grep'ing `useDecisionTables` URL for matching controller revealed 404 — UI tab was broken from day 1, not just "would break on data." Lesson: when fixing FE-driven bugs, always verify the URL exists before assuming DTO is the only delta.

2. **vi.mock module replacement is total.** I imported `deriveIsActive` from the api module in the page; tests mock the module; mock factory must re-export every named symbol the SUT imports or you get a confusing "X is not defined" runtime error. Not a TypeScript compile error — vitest only catches at run.

3. **Identity.Application didn't reference SharedKernel.Contracts.** Module was self-sufficient pre-fix because it never consumed cross-module interfaces. Adding the ProjectReference for Identity module to call its own client (via `IIdentityModuleClient` from inside Identity itself) felt circular — but `InProcessIdentityModuleClient` does direct DB query, no recursion. Still semantically odd: a module exposing `GetUserInfoByIdsAsync` for its own internal use. Stuck with it because precedent exists in Files; refactoring to call repository directly would diverge from established Layer-2 pattern.

## What I Should Remember

- **Dapper DTOs that bind to records with positional ctors break on nullable fields.** Already in memory ([feedback-dapper-no-positional-records.md](../../../Users/thien/.claude/projects/C--AqtCoreFW/memory/feedback-dapper-no-positional-records.md)). The internal `DelegationDtoWithTotal` class in handler is class-typed (correct).
- **FE DTO must match BE controller wire shape.** Already in memory ([feedback-fe-dto-must-match-be-controller.md](../../../Users/thien/.claude/projects/C--AqtCoreFW/memory/feedback-fe-dto-must-match-be-controller.md)). This whole session was that lesson playing out. Worth a re-read before next FE-touching task.
- **Code-reviewer rounds are cheap insurance.** Round 1 caught both drifts. Round 2 caught the cast nit + 3 deferred minors. Cost: ~5 min × 2 invocations. Saved: at minimum the live TypeError on first seed.

## Open Questions

- M-3 (TenantId WHERE in ListDelegations): is tenant scope enforced via global query filter or middleware? Plan didn't audit. Worth a quick scout next session before claiming Identity tenant isolation = solid.
- M-1 (RevokedAt entity column missing) — should this be a migration in v2.133, or wait for an active feature requirement?

## Files Touched

**BE (8 files):**
- `src/modules/rules/AqtCoreFW.Rules.Application/DTOs/RulesDtos.cs`
- `src/modules/rules/AqtCoreFW.Rules.Application/DTOs/DecisionTableJsonHelper.cs` (NEW)
- `src/modules/rules/AqtCoreFW.Rules.Application/Queries/ListDecisionTablesByRuleSet/` (NEW folder)
- `src/modules/rules/AqtCoreFW.Rules.Application/Queries/GetDecisionTable/GetDecisionTableQueryHandler.cs`
- `src/modules/rules/AqtCoreFW.Rules.Application/Commands/CreateDecisionTable/CreateDecisionTableCommandHandler.cs`
- `src/modules/rules/AqtCoreFW.Rules.Presentation/Controllers/RuleSetsController.cs`
- `src/modules/identity/AqtCoreFW.Identity.Application/AqtCoreFW.Identity.Application.csproj`
- `src/modules/identity/AqtCoreFW.Identity.Application/Queries/ListDelegations/ListDelegationsQuery.cs`
- `src/modules/identity/AqtCoreFW.Identity.Application/Queries/ListDelegations/ListDelegationsQueryHandler.cs`
- `tests/unit/AqtCoreFW.Rules.Application.Tests/Queries/ListDecisionTablesByRuleSetQueryHandlerTests.cs` (NEW)

**FE (5 files):**
- `web/src/features/delegations/delegation-api.ts`
- `web/src/features/delegations/delegation-list-page.tsx`
- `web/src/features/rules/rule-set-detail-page.tsx`
- `web/src/features/rules/rules-api.ts` (verified — no change needed; already correct shape)
- `web/src/test/features/delegations/delegations-page.test.tsx`
- `web/src/test/features/phase5/smoke-phase5-pages.test.tsx`

**Docs (5 files):**
- `docs/project-roadmap.md` (header + 2 rows added: v2.132p2 + v2.132p1)
- `docs/project-changelog.md` (v2.132p2 entry inserted above v2.132p1)
- `docs/dependency-map.md` (header version + Identity client registry note)
- `memory/project-state.md` (frontmatter description + Last Updated + Version section + Recent Merges row)
- `memory/MEMORY.md` (project-state row description aligned)

## Pattern Carry-Forward

- When fixing a code-reviewer drift, **grep the FE hook URL** before assuming DTO is the only delta. Endpoint may not exist.
- When extending a record DTO, **check the consuming `vi.mock` factories** — they must re-export new helpers used by the page.
- **Cross-module Layer 2 enrichment via IIdentityModuleClient** is now precedented in 4+ handlers (Files, Workflow, Cases, Identity-self) — established pattern, not novel.
