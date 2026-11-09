# RB5 Live-Claimant Gate at Activation — WF-PARALLEL-RB5-RESOLVE-TO-CLAIMANT

**Date:** 2026-06-08 · **Session:** `/cook WF-PARALLEL-RB5-RESOLVE-TO-CLAIMANT --auto` (5th cook of the day)
**Outcome:** SHIPPED — `49150b16` fix + `e353032b` docs, pushed origin+bitbucket (ahead=0).
**Type:** prod fix (security/correctness) — Workflow.Application graph validator.

## What shipped
RB5-Activate previously checked only that a parallel-branch child carried a clause of a claimable mode
(ByPermission/ByRole) — not that the pool resolved to ≥1 real claimant. A ByRole pool on a role no active
user holds activated clean, forked at runtime into an unclaimable task, and permanently blocked the
AND-join → instance silently stranded InProgress. (Proven reachable the prior session by
`ForkChildPoolWithUnheldRole`.)

Fix: `WorkflowGraphValidator.ValidateForActivateAsync` → async + a live-identity claimant hop. For each
child whose claimable clauses are **exclusively ByRole**, resolve role codes via the existing
`IIdentityModuleClient.ResolveActiveUsersInRolesAsync(codes, tenantId)`; 0 active claimants → RB5 422,
def stays inactive.

## Why this is correct (the load-bearing invariant)
The design-time check is correct **only** because it uses the SAME resolution semantics as the runtime
claim. Code-review read both SQLs and confirmed: `ResolveActiveUsersInRolesAsync` and the claim-side
`GetUserRoleCodesAsync` are both DIRECT `AspNetUserRoles` joins (PQ-1 A — no group-inherited / effective-
permission merge), same `IsActive` + tenant filters, same UPPER+TRIM canonicalization. So "resolves to
nobody at activate" ⟺ "no user can claim at runtime." The code comment states this invariant inline and
warns against swapping to an effective-permission lookup (which would silently break the equivalence).

## Scope discipline (the real work was deciding what NOT to do)
- **ByPermission NOT checked** — no reverse permission→users lookup exists; building it is the deferred
  `WF-NEXT-HANDLER-PERMISSION-USERS` epic (3-path query). A child with ANY ByPermission clause is
  conservatively skipped: its pool may have holders we cannot enumerate, so rejecting it = false-reject.
- **Empty-refs ByRole** → left to the existing runtime fork-entry empty-pool guard (not RB5's job).
- **AllowAssignorOverride NOT consulted** — grep proved the branch-child override guard is an unwired
  later phase (`WorkflowBranchChildAssignment` doc: "Consumed by … later phase"; `ClaimTaskCommandHandler`
  has no override path). So a 0-claimant pool is genuinely unrecoverable today. The code carries a
  forward-note: if that guard ships, add `&& !child.Assignment.AllowAssignorOverride` (an overridable pool
  is recoverable and must not be rejected). **This was the advisor's #2 catch — a discriminating check
  that would have changed the fix had the override been wired.**
- **Activate-only** (Save unchanged — membership drifts between save→activate; activate is the gate).
- **Residual, documented:** a role drained AFTER activation re-opens the deadlock → the deferred H1
  branch reconciler is the runtime backstop. This fix is design-time only.

## Decisions that mattered
- **Advisor #1 (biggest catch): the success branch was unprovable in integration.** Synthetic test users
  have no real `AspNetUserRoles` rows, so a "held role → activates" case can't be seeded there — and the
  only ByRole integration test *expects* rejection. A bug rejecting EVERY ByRole pool would have passed
  every integration test. → Added a unit suite mocking `ResolveActiveUsersInRolesAsync` (held→pass,
  unheld→RB5-names-role, lowercase→canonicalizes-before-resolve via `Received(1)`, ByPermission/mixed→
  skip via `DidNotReceive`). The unit layer is the ONLY place the success branch is provable.
- **Behavior change to an existing test, surfaced not hidden.** The fix converts the prior
  `ForkChildPoolWithUnheldRole` (which proved the runtime deadlock) into a rejected-at-activation test.
  Rewrote it to assert 422 + RB5 reason + role code + def inactive (non-vacuous), and updated the seed
  (`SeedSavedParallelDefinitionAsync` = create+save-no-activate) + all stale doc comments.
- **Low-1 applied (consistency, not gold-plating):** `ParseRoleCodes`' bare `JsonSerializer.Deserialize`
  could surface a 500 on malformed-at-rest JSON (unreachable via Save, but the activate-RB5 structural
  pass does NOT re-validate JSON, so the hop is the only parse at activate). Wrapped it to emit a clean
  RB5 violation — mirrors the file's own R3 malformed-JSON convention.
- **Low-2 (N+1 identity round-trip) NOT fixed** — code-review's own recommendation: true batching needs a
  "which codes have ≥1 holder" coverage-resolver that doesn't exist; activation is a rare admin op with a
  small child count. Address-and-dismiss.

## Verification
Build 0W/0E (prod + both test projects). Unit 59/59 (5 new claimant facts). Integration NegativeTests
3/3 (isolation). RuntimeTests 3/4 — the 1 fail is the pre-existing documented `WFE-RATELIMIT-TEST-DISABLE`
429 flake on a ByPermission happy-path `ClaimAsync` (the same test that filed that backlog row today),
unrelated to ByRole activation validation; the 3 passing Runtime facts confirm no-false-reject for
ByPermission. Code-review APPROVE_WITH_CONCERNS, 0 Crit / 0 High.

## Reusable lessons
- **Reverse-lookup checks are only correct if they mirror the forward path's exact semantics.** A
  design-time "is this claimable" gate that used effective-permissions instead of direct-roles would have
  *passed its own tests* while diverging from runtime claim eligibility. Cite the SQL symmetry in the code.
- **When integration structurally can't cover the success branch, unit-test it — don't ship a one-sided
  guard.** "Happy path stays green" proved nothing here because every happy path was ByPermission; the new
  ByRole *accept* branch never executed in integration.
- **Before a 0-claimant=deadlock premise, check for a recovery escape (override/reassign).** The fix would
  have false-rejected valid configs if branch-child `AllowAssignorOverride` were wired. It isn't — but that
  was a grep away from changing the whole design.

## Carry-forward
- **H1 branch reconciler** (P5-FOLLOWUP remaining open) — the runtime backstop for post-activation role
  drift + timeout/transient strands. This fix closes only the design-time config case.
- **WFE-RATELIMIT-TEST-DISABLE** (T4) — the integration flake still un-rootcaused.
- Optional: when the branch-child assign/reassign override guard ships, revisit the RB5 skip condition.
