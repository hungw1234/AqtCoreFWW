// Converters between DTOs and graph input types for SaveDefinitionGraph API

import type {
  WorkflowStateDto,
  WorkflowTransitionDto,
  GraphStateInput,
  GraphTransitionInput,
} from '../workflow-types';

/** Convert WorkflowStateDto[] to GraphStateInput[] (drop id, keep all fields) */
export function statesToGraphInputs(states: WorkflowStateDto[]): GraphStateInput[] {
  return states.map((s) => ({
    name: s.name,
    displayNameVi: s.displayNameVi,
    displayNameEn: s.displayNameEn,
    isInitial: s.isInitial,
    isFinal: s.isFinal,
    color: s.color,
    sortOrder: s.sortOrder,
  }));
}

/** Convert WorkflowTransitionDto[] to GraphTransitionInput[] (resolve stateId → stateName) */
export function transitionsToGraphInputs(
  transitions: WorkflowTransitionDto[],
  states: WorkflowStateDto[],
): GraphTransitionInput[] {
  const stateMap = new Map(states.map((s) => [s.id, s.name]));
  return transitions
    .filter((t) => stateMap.has(t.fromStateId) && stateMap.has(t.toStateId))
    .map((t) => ({
      fromStateName: stateMap.get(t.fromStateId)!,
      toStateName: stateMap.get(t.toStateId)!,
      actionName: t.actionName,
      actionLabelVi: t.actionLabelVi,
      actionLabelEn: t.actionLabelEn,
      conditionsJson: t.conditionsJson,
      sortOrder: t.sortOrder,
      assignment: t.assignment ?? null,
    }));
}
