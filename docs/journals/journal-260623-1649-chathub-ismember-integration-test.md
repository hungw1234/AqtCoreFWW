# Journal — Collaboration ChatHub JoinConversation Membership Gate Integration Test

**Date:** 2026-06-23 16:49 ICT · **Skill:** `/cook COLLAB-CHATHUB-ISMEMBER-INTEGRATION-TEST` · **Commits:** `1eb3e32b` (test), `f26a0a47` (docs) — pushed origin + bitbucket.

## Session goal

Close backlog row `COLLAB-CHATHUB-ISMEMBER-INTEGRATION-TEST` (T4, test-only): the `ChatHub.JoinConversation`
method's membership gate (`IConversationRepository.IsMemberAsync`) had no direct integration test, despite
being a critical security boundary on a real-time hub.

## What worked

- **Empirical audit before opening (INVARIANT 260522-A) confirmed real-ready.** Probed: ChatHub gate exists
  (`if(!await _repo.IsMemberAsync(...))`)? Yes. IsMemberAsync exists and is hand-rolled with IgnoreQueryFilters
  + manual tenant/soft-delete predicates? Yes. Integration test infra (WebAppFixture + SqlServerCollection)?
  Yes. Verdict: not blocked, not stale — ready to implement.
  
- **Scope: SignalR hub integration (not repo-level).** The standard pattern for testing a hub's server logic is
  to instantiate the REAL ChatHub with REAL EF repo + Testcontainers SQL, and substitute only the transport
  boundary: synthetic `HubCallerContext` (carries JWT `sub`/`tenant_id` claims) + NSubstitute `IGroupManager`
  (spy on AddToGroupAsync). `Hub.Context` and `Hub.Groups` are settable precisely for this. This exercises the
  full claim-extraction → IsMemberAsync (hand-written predicates) → throw/AddToGroup path — no mock can verify
  the EF semantics without real SQL.

- **4 integration tests cover the decision tree:**
  1. Member → `JoinConversation` succeeds, `IGroupManager.AddToGroupAsync` called with conversation group.
  2. Same tenant, non-member → `HubException("Not a member")`.
  3. Cross-tenant attack: same-tenant member's userId + WRONG tenant claim → `HubException`. (This test 
     isolates the `c.TenantId` predicate from the userid match — not a blanket "any wrong guid" reject.)
  4. Missing claims (null tenant) → `HubException` (hub-level short-circuit, before repo call).

- **Teeth-proof (red-before-green invariant).** Temporarily disabled the gate (`if(false)`), ran suite →
  exactly 3 RED (deny paths) + 1 PASS (member path). Re-enabled, all 4 GREEN. Differential coverage proven;
  not a tautology.

## What didn't / lessons

- **Soft-delete branch coverage deferred intentionally.** The `!m.IsDeleted` and `!c.IsDeleted` predicates in
  `IsMemberAsync` have no coverage path today — no member-removal or conversation-hard-delete feature exists
  to set those flags. Code-reviewer flagged as carry-forward (N2): add a deny test when a member-removal
  feature lands. Rationale: test should follow feature, not speculate.

## Decisions made

- **Real SQL integration, not mocks.** The hand-rolled predicates (tenant, soft-delete, FK-verified) are
  database semantics — mocking them away defeats the test.
- **Transport boundary substitution only.** NSubstitute `IGroupManager` (outbound Clients/Groups calls) +
  synthetic `HubCallerContext` (inbound claims). Everything else real.
- **Carry soft-delete coverage to member-removal PR** rather than add false-green assertions today.

## Test status

Build 0 errors/warnings · Collaboration.Integration **4/4 new tests** (~19–27s with real Docker SQL) ·
Architecture guards **145/145** · no public-contract change. code-reviewer: Status DONE, 0 blocking,
1 nit N2 (carry soft-delete to future PR, applied).

## Next session

- No carry-over; this task shipped complete.
- Backlog item resolved in full.
