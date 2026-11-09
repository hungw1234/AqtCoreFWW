---
title: Test Cases - Workflow Module
snapshot_commit: a593d719
snapshot_version: v2.137p113
date: 2026-05-26
ucs_covered: 7
tcs_total: 40
trace_source: docs/srs/use-cases/uc-workflow.md
---

# Test Cases — Workflow Module

Đặc tả test case (IEEE 829-style compact) cho 7 use case core của Workflow module. Mỗi TC trace ngược về UC source + step number, dùng cho QA execution thủ công và làm input cho dev viết test code automation sau. UCs 08/09/10 (Approve/Reject/Return) gộp thành một section Complete-Task với outcome variants.

## Conventions

- **TC-ID format:** `TC-WF-{UC#}-{TC#}` — Module-WF, UC số 02-11, TC seq 01-NN
- **Priority:** P0=Critical (runtime core) | P1=High (safety/state integrity) | P2=Medium (edge/admin) | P3=Low (cosmetic)
- **Type:** Positive (happy path) | Negative (error path) | Boundary (limit values) | Security (auth/authz/race) | Performance (latency)
- **Layer:** Unit | Integration (DB+HTTP) | E2E (FE+BE+infra) | Manual (UX/visual)
- **Trace:** UC-WF-XX step Y-Z links back to source UC scenario step

## Coverage Summary

| UC | Title | TCs | P0 | P1 | P2 |
|----|-------|-----|----|----|----|
| UC-WF-02 | Save Workflow Graph | 6 | 2 | 3 | 1 |
| UC-WF-03 | Publish Definition | 5 | 1 | 3 | 1 |
| UC-WF-04 | Create Instance | 5 | 2 | 2 | 1 |
| UC-WF-05 | View Inbox (Predicate-Pool) | 7 | 3 | 3 | 1 |
| UC-WF-06 | Claim Task (Race-Safe) | 7 | 3 | 3 | 1 |
| UC-WF-08-10 | Complete Task (Approve/Reject/Return) | 6 | 2 | 3 | 1 |
| UC-WF-11 | Cancel Instance (Recall) | 4 | 1 | 2 | 1 |
| **Total** | | **40** | **14** | **19** | **7** |

*Scoped UCs: 14-UC source narrowed to 7 for target density. Dropped: UC-WF-01 (design draft CRUD), UC-WF-07 (unclaim trivial), UC-WF-12/13/14 (admin reassign/delegate/analytics). See Unresolved Questions #1.*

---

## UC-WF-02: Lưu Workflow Graph (Save Transition + Assignment Rules)

Source: `uc-workflow.md#uc-wf-02` — Endpoint `POST /api/v1/workflow/definitions/{id}/save-graph`.

#### TC-WF-02-01: Save graph — happy path (R1-R4 pass)
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-7
- **Pre:** Admin authenticated + perm `Workflow.Def.Manage`; definition in draft (IsPublished=false); 3 states, 2 transitions, 1 assignment referencing valid role `APPROVER` in Identity
- **Data:** `POST /definitions/{id}/save-graph` with states=[DRAFT(initial), REVIEW, APPROVED(final)], transitions=[DRAFT→REVIEW, REVIEW→APPROVED], assignments=[stateId=REVIEW, type=ROLE, value=APPROVER]
- **Expected:** 200 OK; DB `WorkflowState` (3 rows) + `WorkflowTransition` (2 rows) + `WorkflowAssignment` (1 row) persisted in single TX; `WorkflowDefinition.ModifiedAt` updated; audit `WorkflowGraphSavedEvent` created

#### TC-WF-02-02: Save graph — R1 unreachable state rejected
- **Pri:** P0 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 2a
- **Pre:** Admin authenticated; definition in draft
- **Data:** Graph với isolated state `ORPHAN` không có incoming transition từ initial state
- **Expected:** 422 Unprocessable; error detail `"State 'ORPHAN' not reachable from initial state"`; NO DB write (transaction rolled back); audit fails record

#### TC-WF-02-03: Save graph — R4 permission code normalized uppercase
- **Pri:** P1 | **Type:** Boundary | **Layer:** Integration | **Trace:** step 4 (normalization)
- **Pre:** Admin authenticated; valid graph except assignment có perm code lowercase `"wf_task_approve"`
- **Data:** Assignment `{ type:"PERMISSION", value:"wf_task_approve" }`
- **Expected:** 200 OK; DB `WorkflowAssignment.Value = "WF_TASK_APPROVE"` (uppercase normalized per R4); no validation error for known code

#### TC-WF-02-04: Save graph — R3 role không tồn tại trong Identity
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 2c
- **Pre:** Admin authenticated; definition valid except assignment references `"GHOST_ROLE"` absent from Identity
- **Data:** Assignment `{ type:"ROLE", value:"GHOST_ROLE" }`
- **Expected:** 422 Unprocessable; error `"Role 'GHOST_ROLE' not found in Identity module"`; IIdentityModuleClient.GetRoleByCodeAsync called with tenantId param; no DB write

#### TC-WF-02-05: Save graph — PreviousHandler populated on assignment (RT2-15 Phase 01)
- **Pri:** P1 | **Type:** Positive | **Layer:** Integration | **Trace:** step 3 (memory anchor [[monday-260524-am-bp-fix-and-rt2-15-phase-01]])
- **Pre:** Multi-state graph with ≥2 sequential handler states; definition previously had an approved handler at state REVIEW
- **Data:** Save graph với assignment payload `{ "previousHandlerId": "<userId-of-prior-handler>", "stateId": "APPROVED", ... }`
- **Expected:** 200 OK; `WorkflowAssignment.PreviousHandlerId` persisted; audit records assignment payload including previousHandlerId; subsequent instance creation reads this field correctly (verify via UC-WF-04 follow-up query)

#### TC-WF-02-06: Save graph — thiếu perm
- **Pri:** P2 | **Type:** Security | **Layer:** Integration | **Trace:** authz
- **Pre:** User authenticated nhưng KHÔNG có `Workflow.Def.Manage`
- **Data:** Same valid graph body
- **Expected:** 403 Forbidden; no DB write; audit `AccessDenied/perm=Workflow.Def.Manage`

---

## UC-WF-03: Publish Workflow Definition

Source: `uc-workflow.md#uc-wf-03` — Endpoint `POST /api/v1/workflow/definitions/{id}/publish`.

#### TC-WF-03-01: Publish — happy path
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-7
- **Pre:** Admin authenticated + perm `Workflow.Def.Manage`; definition in draft; graph saved (R1-R4 passed via TC-WF-02-01)
- **Data:** `POST /definitions/{id}/publish`
- **Expected:** 200 OK; DB `IsPublished=true`, `IsActive=true`; definition immutable (no further direct edits); audit `WorkflowDefinitionPublishedEvent` (def_id, version)

#### TC-WF-03-02: Publish — R1-R4 re-validation fails at publish time
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** step 2-3 + ext 2a
- **Pre:** Admin managed to bypass SaveGraph (direct DB write) leaving unreachable state; definition still draft
- **Data:** `POST /definitions/{id}/publish`
- **Expected:** 422 Unprocessable; message `"Cannot publish: definition has validation errors"`; `IsPublished` remains false; same validator as SaveGraph invoked (DRY per fail-CLOSED invariant 260523-D)

#### TC-WF-03-03: Publish — attempt edit after published blocked
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 4a
- **Pre:** Definition already published (IsPublished=true)
- **Data:** `PUT /definitions/{id}` với any change
- **Expected:** 403 Forbidden; message `"Published definition immutable; create new version"`; no DB change

#### TC-WF-03-04: Publish — in-flight instances not retroactively affected
- **Pri:** P1 | **Type:** Positive | **Layer:** Integration | **Trace:** step 8 (version isolation)
- **Pre:** Instance A running on Definition v1 (unpublished); Definition v2 now being published
- **Data:** Publish Definition v2; then query Instance A's definition reference
- **Expected:** Instance A still references v1 definition_id; Instance A transitions use v1 graph; v2 graph used ONLY for new instances created after publish

#### TC-WF-03-05: Publish — thiếu perm
- **Pri:** P2 | **Type:** Security | **Layer:** Integration | **Trace:** authz
- **Pre:** User NOT having `Workflow.Def.Manage`
- **Data:** `POST /definitions/{id}/publish`
- **Expected:** 403; definition remains draft; audit `AccessDenied`

---

## UC-WF-04: Khởi tạo Workflow Instance (Create Instance)

Source: `uc-workflow.md#uc-wf-04` — Endpoint `POST /api/v1/workflow/instances`.

#### TC-WF-04-01: Create instance — happy path với ByRole predicate
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-7
- **Pre:** User authenticated + perm `Workflow.Instance.Manage`; definition published; assignment rule ByRole=APPROVER
- **Data:** `POST /workflow/instances { "entityType": "FormSubmission", "entityId": "<guid>", "definitionId": "<published-def-id>", "slaHours": 24 }`
- **Expected:** 201 Created; `WorkflowInstance` row (Status=Open, AssigneeId=null, SlaDate=now+24h); `WorkflowTaskCandidateIndex` rows created với Type=ROLE, Value=APPROVER; Outbox event `WorkflowInstanceCreatedEvent` queued; response contains instanceId + taskId + state

#### TC-WF-04-02: Create instance — ByGroup + ByOrgUnit predicates seeded
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 4-5 (memory anchor [[monday-pm-260525-readside-extension-group]])
- **Pre:** Definition có assignment rules: ByGroup=FINANCE_REVIEWERS AND ByOrgUnit=BRANCH_HN
- **Data:** Same create payload
- **Expected:** 201 Created; CandidateIndex has rows: (Type=GROUP, Value=FINANCE_REVIEWERS) + (Type=ORGUNIT, Value=BRANCH_HN); both predicates stored, OR-merge at inbox query time

#### TC-WF-04-03: Create instance — definition inactive
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 2a
- **Pre:** Definition IsActive=false hoặc không tồn tại
- **Data:** `definitionId` pointing to unpublished definition
- **Expected:** 404 or 422 "Workflow definition not active"; no instance created; no Outbox event

#### TC-WF-04-04: Create instance — atomic rollback khi Identity unavailable
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 4a
- **Pre:** Identity module mocked to return error on GetUserRoleNamesAsync
- **Data:** Valid create payload; candidate resolution step fails
- **Expected:** Instance NOT committed (atomic rollback per NFR); log warning "candidate resolution failed"; 500 or 503 response with retry-friendly error; no partial state in DB

#### TC-WF-04-05: Create instance — cross-tenant definition rejected
- **Pri:** P2 | **Type:** Security | **Layer:** Integration | **Trace:** authz tenant boundary (INVARIANT 260525-E)
- **Pre:** User in tenant-a; `definitionId` belongs to tenant-b
- **Data:** `POST /instances` với cross-tenant definitionId
- **Expected:** 404 (not 403 — no info leak); no instance created; audit `AccessDenied/cross_tenant`

---

## UC-WF-05: Xem Inbox (Predicate-Pool Resolution)

Source: `uc-workflow.md#uc-wf-05` — Endpoint `GET /api/v1/workflow/tasks/inbox`.

#### TC-WF-05-01: Inbox — ByRole predicate match
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 2-4
- **Pre:** User has role APPROVER; task T1 has CandidateIndex(Type=ROLE, Value=APPROVER); task T2 has CandidateIndex(Type=ROLE, Value=MANAGER)
- **Data:** `GET /workflow/tasks/inbox`
- **Expected:** 200 OK; response tasks includes T1 only (APPROVER match); T2 NOT included; IIdentityModuleClient.GetUserRoleNamesAsync called with `(userId, tenantId, ct)` — tenantId param present (INVARIANT 260525-E)

#### TC-WF-05-02: Inbox — ByGroup predicate match (RT2-15 EXT-GROUP)
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 2 (memory anchor [[monday-pm-260525-readside-extension-group]])
- **Pre:** User member of group FINANCE_REVIEWERS; task T1 CandidateIndex(Type=GROUP, Value=FINANCE_REVIEWERS)
- **Data:** `GET /workflow/tasks/inbox`
- **Expected:** 200 OK; T1 in response; IIdentityModuleClient.GetUserGroupCodesAsync called with `(userId, tenantId, ct)` — tenantId param present; group-match row included in OR predicate

#### TC-WF-05-03: Inbox — OR-merge: user matches via role OR group
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 3 (predicate OR logic)
- **Pre:** User has role APPROVER AND group FINANCE_REVIEWERS; task T1 has ONLY CandidateIndex(Type=GROUP, Value=FINANCE_REVIEWERS); task T2 has ONLY CandidateIndex(Type=ROLE, Value=APPROVER)
- **Data:** `GET /workflow/tasks/inbox`
- **Expected:** 200 OK; BOTH T1 and T2 returned (OR across candidate types); DISTINCT — T1 not duplicated even if multi-predicate matches

#### TC-WF-05-04: Inbox — cross-tenant isolation returns empty
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** step 3 WHERE TenantId clause (INVARIANT 260525-E)
- **Pre:** 5 tasks in tenant-b with ROLE=APPROVER; user in tenant-a also has APPROVER role
- **Data:** `GET /workflow/tasks/inbox` from tenant-a user
- **Expected:** 200 OK với tasks=[] (empty); tenant-b tasks NOT leaked; no error; audit `InboxQueryEvent` logged with task_count=0

#### TC-WF-05-05: Inbox — ByUser direct assignment
- **Pri:** P1 | **Type:** Positive | **Layer:** Integration | **Trace:** step 3 (CandidateIndex Type=USER)
- **Pre:** Task T1 CandidateIndex(Type=USER, Value=userId-of-current-user); user has no special role/group
- **Data:** `GET /workflow/tasks/inbox`
- **Expected:** 200 OK; T1 returned via USER predicate match; verifies direct-user assignment path independent of role/group

#### TC-WF-05-06: Inbox — claimed tasks (AssigneeId not null) excluded từ pool view
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** step 3 (Status filter)
- **Pre:** Task T1 đã claimed by user2 (AssigneeId=user2); CandidateIndex still has ROLE=APPROVER; current user has APPROVER
- **Data:** `GET /workflow/tasks/inbox`
- **Expected:** T1 NOT returned (task already assigned; should not appear in unclaimed pool inbox); Status=InProgress + AssigneeId non-null → filtered out

#### TC-WF-05-07: Inbox — thiếu perm
- **Pri:** P2 | **Type:** Security | **Layer:** Integration | **Trace:** ext 1a
- **Pre:** User authenticated but no `Workflow.Task.Process` perm
- **Data:** `GET /workflow/tasks/inbox`
- **Expected:** 403 Forbidden; message `"Permission Workflow.Task.Process required"`; audit `AccessDenied`

---

## UC-WF-06: Claim Task (Self-Assignment, Race-Safe)

Source: `uc-workflow.md#uc-wf-06` — Endpoint `POST /api/v1/workflow/tasks/{id}/claim`.

#### TC-WF-06-01: Claim task — happy path
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-7
- **Pre:** Task T1 exists, Status=Open, AssigneeId=null; user eligible (APPROVER role matches CandidateIndex); perm `Workflow.Task.Process`
- **Data:** `POST /tasks/{T1-id}/claim`
- **Expected:** 200 OK; DB `WorkflowTask.AssigneeId=userId`, `Status=InProgress`; Outbox event `TaskClaimedEvent` published; response `{ taskId, assigneeId, claimedAt }`; audit `TaskClaimed`

#### TC-WF-06-02: Claim task — concurrent race (WinnerFirstInterceptor / ClaimGuard)
- **Pri:** P0 | **Type:** Security | **Layer:** Integration | **Trace:** ext 3b (memory anchor [[p7-cleanup-batches-1-and-2-260522]] DEFER-3 F7r)
- **Pre:** Task T1 AssigneeId=null; two concurrent requests from User A and User B arrive simultaneously; WinnerFirstInterceptor active
- **Data:** Two parallel `POST /tasks/{T1-id}/claim` from different users; simulate via test interceptor ordering with WinnerFirstInterceptor
- **Expected:** Exactly 1 request succeeds (200 OK, AssigneeId set); other receives 409 Conflict "Task claimed concurrently; please retry"; ClaimGuardException(ClaimGuardType.ClaimRace) thrown by interceptor; DB AssigneeId = winner's userId only; no double-claim state

#### TC-WF-06-03: Claim task — đã được claimed bởi user khác (non-race 409)
- **Pri:** P0 | **Type:** Negative | **Layer:** Integration | **Trace:** ext 3a
- **Pre:** Task T1 AssigneeId=user2 (already claimed sequentially)
- **Data:** `POST /tasks/{T1-id}/claim` từ user3
- **Expected:** 409 Conflict; message `"Task already claimed by another user"`; DB unchanged; audit `ClaimAttemptFailed/reason=already_claimed`

#### TC-WF-06-04: Claim task — idempotent self-reclaim
- **Pri:** P1 | **Type:** Positive | **Layer:** Integration | **Trace:** NFR Idempotency
- **Pre:** Task T1 AssigneeId=userId (user đã claim trước đó)
- **Data:** Same user sends `POST /tasks/{T1-id}/claim` again (retry scenario)
- **Expected:** 200 OK (idempotent); AssigneeId unchanged; no duplicate event published; no error

#### TC-WF-06-05: Claim task — task không tồn tại hoặc wrong tenant
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** authz tenant boundary (INVARIANT 260525-E)
- **Pre:** Task T1 belongs to tenant-b; requesting user in tenant-a
- **Data:** `POST /tasks/{T1-id}/claim`
- **Expected:** 404 (not 403); no claim; audit cross-tenant attempt

#### TC-WF-06-06: Claim task — task Status=Completed (closed)
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** state-machine guard
- **Pre:** Task T1 Status=Completed (terminal state)
- **Data:** `POST /tasks/{T1-id}/claim`
- **Expected:** 409 Conflict hoặc 422; message "Cannot claim completed task"; no AssigneeId change; state-transition guard prevents reopening terminal tasks

#### TC-WF-06-07: Claim task — thiếu perm
- **Pri:** P2 | **Type:** Security | **Layer:** Integration | **Trace:** authz
- **Pre:** User authenticated, no `Workflow.Task.Process`
- **Data:** `POST /tasks/{T1-id}/claim`
- **Expected:** 403 Forbidden; audit `AccessDenied/perm=Workflow.Task.Process`

---

## UC-WF-08-10: Hoàn tất Task (Approve / Reject / Return variants)

Source: `uc-workflow.md#uc-wf-08` + `uc-wf-09` + `uc-wf-10` — Endpoint `POST /api/v1/workflow/tasks/{id}/complete`.

#### TC-WF-08-01: Complete task — Approve (happy path + state transition)
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-5 (UC-WF-08)
- **Pre:** Task T1 assigned to current user (AssigneeId=userId); definition has transition REVIEW→APPROVED on APPROVE outcome; user perm `Workflow.Task.Process`
- **Data:** `POST /tasks/{T1-id}/complete { "outcome": "APPROVE", "comment": "LGTM" }`
- **Expected:** 200 OK; instance transitions to APPROVED state; task Status=Completed; new task created for next state (if any per definition); Outbox `TaskCompletedEvent` + `InstanceStateChangedEvent` published; audit `TaskCompleted/outcome=APPROVE`

#### TC-WF-08-02: Complete task — Reject (requeue to pool)
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-5 (UC-WF-09)
- **Pre:** Task T1 in REVIEW state; definition has transition REVIEW→DRAFT on REJECT; assignment rule at DRAFT = ByUser(submitterId)
- **Data:** `POST /tasks/{T1-id}/complete { "outcome": "REJECT", "reason": "Missing docs" }`
- **Expected:** 200 OK; instance returns to DRAFT state; new task created for submitter re-submission; CandidateIndex for new task has Type=USER, Value=submitterId; audit `TaskCompleted/outcome=REJECT`

#### TC-WF-08-03: Complete task — Return to previous state
- **Pri:** P1 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1-5 (UC-WF-10, PreviousHandler resolution RT2-15)
- **Pre:** Task T1 in MANAGER_REVIEW state; definition has RETURN transition back to APPROVER_REVIEW; `previousHandlerId` populated via TC-WF-02-05
- **Data:** `POST /tasks/{T1-id}/complete { "outcome": "RETURN", "comment": "Needs re-review at L1" }`
- **Expected:** 200 OK; instance transitions to previous state; new task created with AssigneeId or CandidateIndex referencing `previousHandlerId` from assignment payload; audit `TaskCompleted/outcome=RETURN`

#### TC-WF-08-04: Complete task — invalid state transition rejected
- **Pri:** P1 | **Type:** Negative | **Layer:** Integration | **Trace:** state-machine guard
- **Pre:** Task T1 in APPROVED (terminal final state); definition has NO outgoing transition from APPROVED
- **Data:** `POST /tasks/{T1-id}/complete { "outcome": "APPROVE" }`
- **Expected:** 422 Unprocessable; message "No valid transition from state APPROVED with outcome APPROVE"; instance state NOT changed; FSM enforces transition closure per R2

#### TC-WF-08-05: Complete task — non-assignee attempt rejected
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** authz ownership
- **Pre:** Task T1 AssigneeId=user2; request từ user3 (different user)
- **Data:** `POST /tasks/{T1-id}/complete { "outcome": "APPROVE" }`
- **Expected:** 403 Forbidden; message "You are not the assignee for this task"; no state transition; audit `AccessDenied/reason=not_assignee`

#### TC-WF-08-06: Complete task — outcome missing / invalid
- **Pri:** P2 | **Type:** Boundary | **Layer:** Integration | **Trace:** validation
- **Pre:** Task T1 assigned to current user; valid state
- **Data:** `POST /tasks/{T1-id}/complete { }` (no outcome field)
- **Expected:** 400 Bad Request; validation error `"outcome is required"`; no transition; no event

---

## UC-WF-11: Hủy Workflow Instance (Cancel / Recall)

Source: `uc-workflow.md#uc-wf-11` — Endpoint `POST /api/v1/workflow/instances/{id}/cancel`.

*Note: "Recall" semantic in task spec mapped to this UC. See Unresolved Questions #2.*

#### TC-WF-11-01: Cancel instance — happy path (submitter cancels own instance)
- **Pri:** P0 | **Type:** Positive | **Layer:** Integration | **Trace:** step 1 (UC-WF-11)
- **Pre:** Instance I1 Status=Open; current user is submitter (CreatedBy=userId); perm `Workflow.Instance.Manage`
- **Data:** `POST /instances/{I1-id}/cancel { "reason": "Submitted by mistake" }`
- **Expected:** 200 OK; instance Status=Cancelled; ALL associated open tasks Status=Cancelled; CandidateIndex rows cleared or marked inactive; Outbox `InstanceCancelledEvent` published; audit `InstanceCancelled/by=submitter/reason=...`

#### TC-WF-11-02: Cancel instance — admin force cancel bất kỳ instance
- **Pri:** P1 | **Type:** Positive | **Layer:** Integration | **Trace:** perm `Workflow.Task.Manage` (admin path)
- **Pre:** Instance I1 Status=InProgress; current user is admin với `Workflow.Task.Manage`; NOT submitter
- **Data:** `POST /instances/{I1-id}/cancel`
- **Expected:** 200 OK; instance cancelled; audit `InstanceCancelled/by=admin`; reassignment logic NOT triggered (cancel terminates; contrast with disable-user UC-IDN-09-04 reassign)

#### TC-WF-11-03: Cancel instance — non-owner non-admin blocked
- **Pri:** P1 | **Type:** Security | **Layer:** Integration | **Trace:** authz ownership
- **Pre:** Instance I1 CreatedBy=user1; request từ user2 WITHOUT admin perm
- **Data:** `POST /instances/{I1-id}/cancel`
- **Expected:** 403 Forbidden; instance state unchanged; audit `AccessDenied/reason=not_owner`

#### TC-WF-11-04: Cancel already-completed instance blocked
- **Pri:** P2 | **Type:** Negative | **Layer:** Integration | **Trace:** state-machine guard
- **Pre:** Instance I1 Status=Completed (terminal)
- **Data:** `POST /instances/{I1-id}/cancel`
- **Expected:** 409 Conflict; message "Cannot cancel completed instance"; no state change; state-machine FSM prevents cancellation of terminal states

---

## Test Data Requirements

Để chạy TC suite cần seed:

| Entity | Records | Notes |
|--------|---------|-------|
| Tenants | tenant-a, tenant-b | Cross-tenant isolation tests |
| Users | submitter01, approver01, approver02, admin01 | Different roles, concurrent claim tests |
| Roles | APPROVER, REVIEWER, MANAGER | Canonical uppercase; linked to perm WF_TASK_APPROVE |
| Groups | FINANCE_REVIEWERS | Group predicate tests TC-WF-05-02/03 |
| OrgUnits | BRANCH_HN | OrgUnit predicate tests TC-WF-04-02 |
| Permissions | Workflow.Def.Manage, Workflow.Instance.Manage, Workflow.Task.Process, Workflow.Task.Manage | Exact codes used in authz checks |
| WorkflowDefinitions | def-v1 (published), def-draft | State: DRAFT→REVIEW→APPROVED transitions |
| WorkflowInstances | i1-open, i1-completed, i1-cancelled | Various status states for state-guard tests |

Tham chiếu `tests/AqtCoreFW.Tests.Integration/Infrastructure/Seeders/E2ETestDataSeeder.cs` cho group membership seeding (TC-WF-05-02 cần `UserGroupMembership` rows).

---

## Execution Notes

- **Manual run:** Postman collection từ Swagger `/swagger/v1/swagger.json`; chạy trong thứ tự UC-WF-02 → 03 → 04 → 05 → 06 → 08-10 → 11 (setup before runtime)
- **Automation roadmap:** TC IDs map 1-1 sang xUnit `[Fact]` method names; TC-WF-06-02 cần `WinnerFirstInterceptor` seeded và parallel Task.WhenAll fixture
- **Cleanup:** Mỗi TC cleanup via `IClassFixture<DatabaseFixture>` reset hoặc rollback; race TCs (TC-WF-06-02) serialize in `[Collection("WF-Claim")]`
- **Race TC setup:** TC-WF-06-02 mirrors `ClaimGuardExceptionDiscriminationTests` pattern from v2.137p95; reuse `WinnerFirstInterceptor` seeding pattern

## Unresolved Questions

1. **UC scoping (7 vs 14):** Task spec says "7 UC headings" but source has 14 UCs. This file covers UC-02/03/04/05/06/08-10/11. Confirm whether UC-WF-07 (Unclaim), UC-WF-12 (Admin Reassign), UC-WF-13 (Delegate), UC-WF-14 (Analytics) warrant separate TC file or append here.
2. **"Recall" semantic:** Task spec mentions `recall removes from candidate pool + audit trail` as a distinct concept but UC file has no UC-WF-RECALL heading. Mapped to UC-WF-11 (Cancel). If "recall" = submitter retracts in-progress instance back to draft (not full cancel), it is an unimplemented UC — needs product + arch confirm.
3. **ByOrgUnit inbox predicate (TC-WF-05):** UC-WF-05 sequence diagram includes `ORGUNIT` CandidateIndex but GetUserOrgUnitCodesAsync is not listed in UC-WF-05 IIdentityModuleClient call list. Confirm whether OrgUnit dimension is resolved via separate cross-module call or from user profile at create-time.
4. **Idempotent claim (TC-WF-06-04):** UC-WF-06 NFR says "same user claim same task twice = idempotent return success" but does not define whether a second `TaskClaimedEvent` is published. Event deduplication expectation needs clarification for downstream consumers.
5. **PreviousHandler storage (TC-WF-02-05 / TC-WF-08-03):** `previousHandlerId` field on `WorkflowAssignment` entity — confirm exact column name and whether it carries over when SaveGraph is called fresh vs incremental update (no partial-update isolation guarantee documented in source UC).
