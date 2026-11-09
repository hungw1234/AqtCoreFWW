// Regression for the per-child pool save mapping: branchToInput must forward child.assignment
// verbatim — both a newly authored pool and (crucially) a pre-existing one carried through an
// unrelated edit. The render-based authoring tests don't cover the "edit something else, save"
// path; this pure-function fact does.

import { describe, it, expect } from 'vitest';
import { branchToInput } from '../workflow-graph-input-utils';
import type { WorkflowBranchDto, AssignmentInput } from '../../workflow-types';

const stateMap = new Map<string, string>([
  ['s1', 'Draft'],
  ['s2', 'Review'],
  ['s3', 'Done'],
]);

const pool: AssignmentInput = {
  combineMode: 'Union',
  allowAssignorOverride: false,
  clauses: [
    { mode: 'ByPermission', refsJson: '["WORKFLOW.TASK.PROCESS"]' },
    { mode: 'ByRole', refsJson: '["BTC_MINISTER"]' },
  ],
};

function branchWith(childAssignment: AssignmentInput | null | undefined): WorkflowBranchDto {
  return {
    id: 'b1',
    branchingStateId: 's1',
    continuationStateId: 's3',
    branchType: 'And',
    requiredCount: null,
    allowCancelOnAnyReject: false,
    timeoutMinutes: 0,
    positionX: null,
    positionY: null,
    children: [
      {
        id: 'c1', targetStateId: 's2', name: 'Review', displayNameVi: 'Xem xét',
        sortOrder: 0, resolutions: [], assignment: childAssignment,
      },
    ],
  };
}

describe('branchToInput — per-child assignment', () => {
  it('forwards a pre-existing child assignment verbatim, clause order preserved', () => {
    const input = branchToInput(branchWith(pool), stateMap);
    expect(input.children[0].assignment).toEqual(pool);
    // Order matters for the BE clause-order round-trip — assert it explicitly.
    expect(input.children[0].assignment?.clauses.map((c) => c.mode)).toEqual(['ByPermission', 'ByRole']);
  });

  it('emits assignment=null when a child has no pool (undefined → null)', () => {
    const input = branchToInput(branchWith(undefined), stateMap);
    expect(input.children[0].assignment).toBeNull();
  });

  it('still resolves the rest of the child (target name) when an assignment is present', () => {
    const input = branchToInput(branchWith(pool), stateMap);
    expect(input.children[0].targetStateName).toBe('Review');
  });
});
