# Journal — Phase-07 PR-1 + PR-2 Shipped (2026-05-06)

**Branch:** `feature/phase-07-group-b-mapper-fix` (stacked on `feature/phase-07-fe-contract-validation`)
**PRs:** #76 (Group A retier), #77 (Group B narrow)
**Commits this session:** `365eb340`, `686d82c0`, `27d435ef`

## Session goal

Close Tier A diagnostic finding (19 FE↔BE contract drifts post Phase-04a/04b entity tier refactor). Triage:
- Group A: 4 admin entities flagged T1 with FE consumer expecting `id: string<uuid>` → retier decision
- Group B: 14 fields suspected of mapper bugs (`projecting entity.Id instead of entity.PublicId`)

## What worked

### PR #76 — Group A retier T2 (`365eb340` + fix-forward `686d82c0`)
- 3 entities retiered T1→T2: `MfaPolicy`, `AiModelProfile`, `AiPromptTemplate`. Each gained `Guid PublicId` AK + factory `IGuidGenerator` injection.
- HasData seeds use deterministic Guid v7 constants (`018f5e80-0001-7001-...`).
- DTOs migrated `Id : long → Guid` (project `entity.PublicId`).
- 4th entity `AuditRetentionPolicy` doc-only — admin governance, no FE consumer; explicit skip rationale embedded in DTO XML doc.
- Tier A re-extract: 3/3 Group A schemas FIXED, +1 envelope drift closed.
- Test state: Architecture 26/26, Identity unit 225/225, AI 112/112.

### PR #77 — Group B narrow (`27d435ef`)
- Original 14-field premise refuted by per-field tier audit (`plans/reports/planner-260506-1251-GH-07-group-b-tier-audit.md`): only **2 of 14** fields target T2 entities.
- Genuine bugs fixed:
  - `CreateWorkflowTaskRequest.workflowInstanceId : long → workflowInstancePublicId : Guid` (WorkflowInstance T2)
  - `CreateMessageLogBody.contractId : long? → contractPublicId : Guid?` (Contract T2)
- Strategy A pattern (Workflow PR #74): handler resolves `entity.PublicId` → `entity.Id` for FK persist.
- **Bonus security fix**: cross-tenant guard added to `CreateMessageLogCommandHandler` (was missing; would let attacker with valid Contract Guid log against another tenant).
- 12 fields targeting T1 by design deferred to task #10 classification audit.
- Test state: Architecture 26/26, Workflow 428/428, Integration 37/37 unit.

### Bonus side-quest (`686d82c0`)
PR-1 build broken by CS0111: stale Identity migration `20260505030140_*` not staged for removal in `365eb340` → duplicate class `InitialCreateFoundationV1` with new `20260506031659_*`. Fix-forward removed 2 stale files; pushed to PR #76. Root cause = git-manager subagent staging gap (deletions not auto-staged).

## What didn't / lessons

- **Premise refutation saved 4-8h** — original Group B brief assumed all 14 fields = mapper bugs. Per-field source audit before implementation showed 12/14 target T1 by design (no PublicId AK to project). Reinforces memory `feedback-tier-rubric-must-include-addressing` — STOP signal to verify entity tier before applying canonical pattern.
- **Testcontainers cold-start** still blocks ALL integration suites (Identity 0/54, Workflow 0/23 in this session). Pre-existing infra issue per memory `feedback-testcontainer-cold-start`. Affects all phases equally — not PR-2 regression. Worth root-cause investigation in dedicated session.
- **fullstack-developer overstepped Phase-03 boundary** in PR-1 — performed Orval regen + dependency-map + journal despite deferred instruction. Damage assessment: edits substantively correct, accepted. Reinforces `feedback-tester-no-self-commit` pattern (subagents respect explicit scope boundaries).

## Decisions made

- **Group A scope = 3 entities, not 4** — `AuditRetentionPolicy` kept T1 (admin governance, no FE consumer). Doc-only DTO comment captures rationale for future revisits.
- **Group B scope narrowed to 2 bugs (Option C Hybrid)** — defer 12-field T1 classification to dedicated session (#10). Avoids architecture decisions stacked at session tail.
- **Stacked PR strategy** — PR #77 base = PR #76 branch (not main). GitHub auto-rebases on merge. Reviewer reads only PR-2 delta.
- **`docker-sqlserver-1` healthcheck miscalibration** — DB serves requests; container reports unhealthy. Cosmetic, not blocking. Backlog candidate.

## Test status

| Suite | Count | Status |
|---|---|---|
| Architecture | 26/26 | PASS |
| Identity unit | 225/225 | PASS |
| AI unit | 112/112 | PASS |
| Workflow unit | 428/428 | PASS |
| Integration unit | 37/37 | PASS |
| Integration tests (any module) | infra-blocked | Testcontainers cold-start |

## Next session priorities

1. **Wait for reviewer merge** PR #76 → PR #77 (stacked, auto-rebase).
2. **QĐ-742 Phase-03 (MFA Policy UI)** — independent work, unblocked. Resume cmd: `/ck:cook plans/260428-1740-cap-4-qd-742-compliance/phase-03-mfa-policy-ui.md --tdd`. PR-1 retier MfaPolicy T2 enables Guid-based UI binding.
3. **Task #10 — 12 T1-target fields classification** (post-merge) — per-entity decide retier vs accept-long: WorkflowTransition, WorkflowState, FormField, FormTemplate, Partner, NotificationTemplate. Excludes AI module (deferred per memory).
4. **Backlog item PHASE7-CONTRACT-CI-GATE** — Tier A re-extract as CI step.

## Tier A drift closure status

| Closed | Open | Total |
|---|---|---|
| 6 (4 PR-1 + 2 PR-2) | 13 (12 T1 classification + 1 read-side symmetry) | 19 |

## Unresolved questions

- Should `MessageLogDto.ContractId` read-side stay `long?` or symmetrize to `Guid?` — deferred (task #12 backlog).
- `WorkflowInstanceRepository.GetByIdAsync` tenant-filter behavior (PR #77 P3-2) — verify in next code-review pass.
- Task #10 — for `Partner` (cross-module FK target T1, no PublicId), choose: (a) retier T1→T2, (b) `Partner.Code` slug as external addressing key, (c) pre-resolve via list endpoint. No precedent in codebase for option (b) outside reporting slug pattern.
