// Pure helpers for fork-branch creation in the visual designer tab.
// Extracted to keep definition-visual-designer-tab.tsx under the 200L ceiling.

import type { WorkflowBranchDto, WorkflowStateDto } from '../workflow-types';

/**
 * Build a new WorkflowBranchDto with a temp client id (crypto.randomUUID()) on the given
 * branching state, defaulting continuation to the final state (or last state).
 */
export function createForkBranch(
  branchingStateId: string,
  states: WorkflowStateDto[],
): WorkflowBranchDto {
  const finalState = states.find((s) => s.isFinal) ?? states[states.length - 1];
  return {
    id: crypto.randomUUID(),
    branchingStateId,
    continuationStateId: finalState?.id ?? branchingStateId,
    branchType: 'And',
    requiredCount: null,
    allowCancelOnAnyReject: false,
    timeoutMinutes: 0,
    positionX: null,
    positionY: null,
    children: [{
      id: crypto.randomUUID(),
      targetStateId: '',
      name: 'NewBranch',
      displayNameVi: 'Nhánh mới',
      sortOrder: 0,
      resolutions: [],
    }],
  };
}
