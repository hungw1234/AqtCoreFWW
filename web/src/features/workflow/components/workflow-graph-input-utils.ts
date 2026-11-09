// Pure helpers for converting DTOs → API input shapes and for auto-layout ordering.
// Extracted from definition-visual-designer-tab.tsx to keep both files under 200 LOC.

import type {
  WorkflowStateDto,
  WorkflowTransitionDto,
  GraphStateInput,
  GraphTransitionInput,
  WorkflowBranchDto,
  GraphBranchInput,
} from '../workflow-types';

/** Convert a single StateDto → GraphStateInput (drops server-assigned id, keeps designer coordinates). */
export function stateToInput(s: WorkflowStateDto): GraphStateInput {
  return {
    name: s.name,
    displayNameVi: s.displayNameVi,
    displayNameEn: s.displayNameEn,
    isInitial: s.isInitial,
    isFinal: s.isFinal,
    color: s.color,
    sortOrder: s.sortOrder,
    positionX: s.positionX ?? null,
    positionY: s.positionY ?? null,
  };
}

/** Convert a single TransitionDto → GraphTransitionInput using a pre-built id→name map. */
export function transitionToInput(
  tr: WorkflowTransitionDto,
  stateMap: Map<string, string>,
): GraphTransitionInput {
  return {
    fromStateName: stateMap.get(tr.fromStateId) ?? tr.fromStateId,
    toStateName: stateMap.get(tr.toStateId) ?? tr.toStateId,
    actionName: tr.actionName,
    actionLabelVi: tr.actionLabelVi,
    actionLabelEn: tr.actionLabelEn,
    conditionsJson: tr.conditionsJson,
    sortOrder: tr.sortOrder,
    isRecall: tr.isRecall,
    sourceHandle: tr.sourceHandle ?? null,
    targetHandle: tr.targetHandle ?? null,
    assignment: tr.assignment ?? null,
  };
}

/**
 * Convert a designer branch (id-keyed, as loaded from GET / authored in the editor) → the name-keyed
 * GraphBranchInput save shape, resolving every state-id reference to its name via the id→name map.
 * The fork drag position passes through unchanged. The branch's own client id is dropped — the BE
 * replace-all assigns fresh ids.
 */
export function branchToInput(
  branch: WorkflowBranchDto,
  stateMap: Map<string, string>,
): GraphBranchInput {
  const nameOf = (stateId: string) => stateMap.get(stateId) ?? stateId;
  return {
    branchingStateName: nameOf(branch.branchingStateId),
    continuationStateName: nameOf(branch.continuationStateId),
    branchType: branch.branchType,
    allowCancelOnAnyReject: branch.allowCancelOnAnyReject,
    timeoutMinutes: branch.timeoutMinutes,
    requiredCount: branch.requiredCount ?? null,
    positionX: branch.positionX ?? null,
    positionY: branch.positionY ?? null,
    children: branch.children.map((c) => ({
      targetStateName: nameOf(c.targetStateId),
      name: c.name,
      displayNameVi: c.displayNameVi,
      sortOrder: c.sortOrder,
      departmentId: c.departmentId ?? null,
      defaultAssigneeId: c.defaultAssigneeId ?? null,
      // Carry the per-child pool through verbatim — preserves a pre-existing assignment when the
      // user edits an unrelated field, and forwards a newly authored one to the replace-all save.
      assignment: c.assignment ?? null,
      resolutions: c.resolutions.map((r) => ({
        resolutionType: r.resolutionType,
        resultingStateName: r.resultingStateId ? nameOf(r.resultingStateId) : null,
      })),
    })),
  };
}