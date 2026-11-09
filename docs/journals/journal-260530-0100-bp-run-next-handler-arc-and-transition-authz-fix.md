# Journal — BP-Run detail next-handler arc + transition-authz parity fix (2026-05-29 → 30)

**Session:** 5 commits, local main, **unpushed** (`origin/main..HEAD` = 5). Working tree clean. 30 files, +853/-66.
Sequence: `6d7c6dc9` → `9936a44e` → `7ffedcb6` → `c0acd952` → `c443e9e0`.

## Session goal
Started as 3 separate `/ck-debug` reports on the BP-run detail page (`/business-process-runs/$id`), each surfacing a deeper gap than the symptom suggested:
1. Two list-tab headers differ → (turned out by-design; led to filing future work).
2. Detail page missing history + next-handler.
3. Next-handler should name the actual users.
4. "No claim button, task stuck" → real cause was transition authz.

## What worked (verified)
- **p128 (`9936a44e`+`6d7c6dc9`):** rendered processing history (reused `TransitionTimeline`, enhanced to show actor/state/action labels) + a new `next-handler-card`. Reshaped `by-instance` to `WorkflowInstanceTaskDto` (assigneeName + candidates), left shared `WorkflowTaskDto` untouched. Gated endpoint `BP.VIEW`. Caught the `JsonStringEnumConverter` string-enum trap (FE status filter was numeric → fixed to strings). Side-fix: restored the red `Workflow.Application.Tests` (orphan-fix p127 added an `IWorkflowTaskRepository` ctor arg, left 4 fixtures on the old signature) → 587/587.
- **p129 (`c0acd952`):** eligible-user list. New `ResolveActiveUsersInRolesAsync` (direct AspNetUserRoles, mirrors forward claim semantics). Discriminating live check: GovOfficer pool showed the **2 active** direct holders — the inactive seed holder was correctly excluded (count 2, not 3), proving the active filter.
- **p130 (`c443e9e0`):** the real "task stuck" fix. `ResolverAuthz` matched permission codes only, ignoring `PrincipalType` → ByRole/ByGroup/ByOrgUnit transitions denied to their holders. Made it type-aware (`ActorPrincipals`/`ActorPrincipalsLoader` mirroring `IsPoolEligibleAsync`), updated all 3 call sites (Available + Execute actor + Execute override). Live: "Duyệt" appeared for the GovOfficer-by-role user; claim flow worked end-to-end (DB AssigneeId set, UI → "Đã giao").
- All ships: build 0E, `Workflow.Application.Tests` 600/600, FE typecheck clean, browser-verified.

## What didn't / lessons
- **Subagent theory contradicted live reality.** A scout claimed transition authz "does NOT check assignee, only pool eligibility" implying pool members could act — but live showed empty "Thao tác". Trusting it would have mis-fixed. Reading `ResolverAuthz` source (permission-only) + the definition data (Approve = ByRole) settled it. Lesson reinforced: live observation > subagent summary; verify load-bearing claims at source.
- **Reframe before fixing.** User reported "missing claim button"; the actual blocker was authz. Claim was a red herring (authz never reads `task.AssigneeId`). Diagnosing the real cause avoided shipping a claim button that wouldn't have unstuck anything.
- **Browser coordinate-click is flaky** when the page re-scrolls on re-render — two coordinate clicks on "Nhận việc" silently missed (DB unchanged, no POST). Element-ref click (`find` → click by ref) hit reliably. Use ref-clicks for post-mutation UI.
- **Interface change = ripple.** Adding `ResolveActiveUsersInRolesAsync` to `IIdentityModuleClient` broke 4 integration-test stubs; adding ctor params to `ExecuteTransitionCommandHandler` broke 4 unit fixtures (twice — p128 taskRepository, then p130 orgUnitClient). Append params trailing to minimize positional churn.
- **Dev-server churn:** each BE build needs Auth/API stopped (Workflow.dll lock); relative `--project` paths failed after bash CWD drifted → use absolute paths. Permissions spinner is slow on cold API restart (settles after a reload).

## Decisions made
- **Two list-tab headers differ = by-design, keep** (user had already ratified "allow overlap"; task-inbox vs instance-history are different entities). Filed `WF-MULTI-INSTANCE-ASSIGNMENT` (T5) for the genuinely-new parallel-assignment pattern.
- **Next-handler scope (user):** Role+Group+OrgUnit now, defer Permission (reverse query is complex); cap eligible list at 20 + count.
- **Authz fix approach A (user):** make authz 4-type-aware (parity with claim) + add claim button. NOT claim-first (B) — wouldn't work as coded.
- **M1 authz on by-instance:** gate `BP.VIEW` (matches the page), not `Task.Process` (would 403 view-only users).

## Test status
`Workflow.Application.Tests` 600/600. Solution build 0 errors. FE typecheck (tsconfig.app) clean. All 3 ships browser-verified on instance `019e6f89…` (BP "2222…", InReview).

## Next session
- **PUSH** the 5 local commits (origin + bitbucket) — all unpushed since p126.
- Permission-pool eligible-user listing (deferred from p129) — needs `ResolveActiveUsersWithPermissionAsync` (direct role + group-inherited + delegation; complex).
- `WF-RECALL-ORPHAN-TASK` (from p127) still open.
- During verify I claimed task 10006 for Dev Admin (valid state, left as-is) — unclaim if the demo instance needs resetting.
