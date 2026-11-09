# Workflow Delegation Proxy (T5-08) — Full Stack Delivery

**Date**: 2026-04-12 15:08
**Severity**: Low
**Component**: Workflow Module — Delegation
**Status**: Resolved

## What Happened

Delivered T5-08 (Workflow delegation/proxy) across 3 gates. BE (G1+G2) shipped 1 new entity (`DelegationRule`), 5 commands, 1 query, 1 Hangfire job (auto-expire), 6 REST endpoints, and permission `WORKFLOW.DELEGATION.MANAGE`. G3 (FE + cleanup + E2E) shipped today.

## Root Cause

New requirement: users need to delegate workflow tasks to other users during absences, with time-bounded rules and ability to return delegation early.

## Fix Applied

**BE (G1+G2):**
- `DelegationRule` entity (soft-delete, overlap validation, TenantId scoped)
- `WorkflowTask.DelegatedFromUserId` nullable Guid tracks original assignee
- Commands: CreateDelegationRule, UpdateDelegationRule, DeleteDelegationRule, DelegateTask, ReturnDelegation
- Query: GetDelegationRulesQuery
- `WorkflowDelegationController` — 6 endpoints under `/api/v1/workflow/delegation-rules` and `/api/v1/workflow/tasks/{id}/delegate|return-delegation`
- Hangfire job: auto-expire active delegations when `validUntil` passes

**G3 — Cleanup:**
- Deleted dead `IWorkflowUnitOfWork` interface (YAGNI — no handler ever injected it)
- Removed impl from `WorkflowDbContext`, removed DI registration

**G3 — FE:**
- 6 React Query hooks (`useDelegationRules`, `useCreateDelegationRule`, `useUpdateDelegationRule`, `useDeleteDelegationRule`, `useDelegateTask`, `useReturnDelegation`)
- 5 new TypeScript types in `workflow-types.ts`
- `DelegationRulesPage` — CRUD table for managing rules
- `DelegationRuleFormDialog` — create/edit modal with dayjs date pickers
- `DelegateTaskDialog` — delegate task modal with delegatee + expiry + reason
- `task-detail-drawer.tsx` — Delegate button + Return Delegation button + delegated-from badge
- Route `/admin/workflow-delegation`, nav entry (permission gate: `WORKFLOW.DELEGATION.MANAGE`)
- i18n: 24 keys in `en.json` + `vi.json`
- E2E spec: `web/e2e/workflow-delegation.spec.ts` — 8 serial tests (TC-DEL-001..007)

## Test Results

- BE build: 0 errors
- Workflow.Application.Tests: 287/287 pass
- Workflow.Domain.Tests: 77/77 pass
- FE typecheck: 0 errors
- E2E spec compile: 8 tests listed

## Lessons Learned

- Split FE delegation UI across 3 files (form-dialog, rules-page, delegate-task-dialog) to stay under 200 LOC/file cap
- React fragment wrapping `DelegateTaskDialog` outside `Drawer` avoids z-index stacking issues
- Self-delegation (delegatorUserId == delegateToUserId) is valid for E2E simplicity in tests
