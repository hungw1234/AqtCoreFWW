# Journal — 2026-06-02 · WF-BYROLE-MATCH-BY-CODE (precursor to P4)

**Commit:** `b5e15310` (v2.137p136), pushed origin+bitbucket. Full-stack refactor, 34 files.

## What
Switched Workflow **ByRole** assignment-clause matching from role **NAME** (`AspNetRoles.Name`, Vietnamese display) to role **CODE** (`AspNetRoles.Code`, stable ASCII e.g. `BTC_MINISTER`).

## Why (discovered mid-`/cook P4`)
While scouting P4 (DemoWorkflowSeeder) the user asked "why does ByRole match by name, not id/code?". Scout of all clause modes proved ByRole was the **only** predicate keying on display-name; Permission/Group/OrgUnit already key on **code**, SpecificUser on id. Name-matching is fragile (rename breaks routing; SQL `UPPER` vs C# `ToUpperInvariant` Vietnamese-diacritic hazard). Code is stable, unique `(Code,TenantId)`, ASCII. P4 would be the **first production ByRole producer** → right moment to fix before baking the fragility in.

## Changes
- `IIdentityModuleClient.GetUserRoleNamesAsync`→`GetUserRoleCodesAsync` (SQL `UPPER(LTRIM(RTRIM(r.Code)))`); `ResolveActiveUsersInRolesAsync` matches `r.Code`.
- `ActorPrincipals.RoleNames`→`RoleCodes`; `ResolverAuthz` Role arm; `WorkflowHandlerResolver` ByRole branch (+ **fixed stale "READ-SIDE GAP / Do NOT seed ByRole" comment** — read-side shipped 2026-05-25).
- `WorkflowGraphValidator.CanonicalizeRoleNamesInPlace`→`...CodesInPlace`.
- `IWorkflowTaskRepository`/`WorkflowTaskRepository` param `roleNames`→`roleCodes`; `ClaimTask`/`GetInbox` handlers.
- FE `workflow-role-multi-select.tsx` select value `r.name`→`r.code` (+ `!!r.code` blank filter); comment fixes.
- Test `IdentityModuleClientRoleNamesTests`→`IdentityModuleClientRoleCodesTests` (rewritten: seeds known Code, asserts code-matching non-tautologically; PQ-1 A direct-only lock preserved). ~17 mock-based ByRole tests = method-rename only (mock in==out).

## Verification
Build `src/AqtCoreFW.slnx` 0E · FE typecheck 0E · **155/155 tests** (120 unit + 27 integration incl. 5 real-SQL role-code proofs + 12 claim/producer chain + 10 graph-save + 8 FE) · code-review APPROVE-WITH-NITS. No prod ByRole data → no migration. Empty-inbox precondition verified: `AspNetRoles.Code` NOT NULL (`ApplicationRoleConfiguration.cs:11`).

## Lessons (META)
1. **User domain-instinct caught a real design smell** — "why by name?" surfaced that ByRole was inconsistent with its 3 sibling modes. Scouting ALL modes (not just ByRole) gave the authoritative table that settled it.
2. **Verify shipped-vs-stale on conflicting comments** — resolver inline comment said "read-side gap, do not seed ByRole" but enum doc + repo query + completed.md (T3-...READSIDE-EXTENSION DONE 2026-05-25) proved read-side shipped. The actual repository query (4-type filter) was the decisive primary source; fixed the misleading comment in the same diff.
3. **Don't trust subagent "no test changes"** — Explore concluded zero test edits; build surfaced 17+ compile breaks (mocks/stubs/asserts). Build-as-oracle for refactor blast radius.
4. **Right-moment refactor** — fixing a shared-contract smell is cheapest before the first production consumer exists (no data migration).

## Carry-forward
- **`/cook P4` DemoWorkflowSeeder** now unblocked — seeds role CODES cleanly. Decisions parked: generic 6-state both tenants · auto-hierarchy ByRole matrix · def=Draft.
- Nits (non-blocking): docs/srs GetUserRoleNames refs; test-stub `roleNames` param cosmetic; MFA policy still keys on role Name (separate subsystem, by design).
