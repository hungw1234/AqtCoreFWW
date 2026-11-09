// Visual designer action handlers — save/undo/auto-layout + add-state + draw-to-connect.
// Extracted from definition-visual-designer-tab.tsx to keep component under LOC ceiling.
//
// Dep-array note: useState setters and refs are guaranteed stable by React, so listing them
// is harmless. `states`/`transitions` ARE real deps of `handleSave` (it reads them into the
// network payload); do NOT drop them or handleSave will serialise stale snapshots.

import { useCallback, createElement, useState } from 'react';
import type { Dispatch, MutableRefObject, SetStateAction } from 'react';

import { useTranslation } from 'react-i18next';
import { appMessage } from '@/core/api/notification-bridge';
import { formatGraphViolations, type GraphViolation } from '@/core/api/api-error';
import type {
  WorkflowStateDto,
  WorkflowTransitionDto,
  WorkflowBranchDto,
} from '../workflow-types';
import { useSaveDefinitionGraph } from '../workflow-api';
import { stateToInput, transitionToInput, branchToInput } from './workflow-graph-input-utils';
import { computeDagreLayout } from './workflow-dagre-layout';
import type { SelectedElement } from './properties-sidebar';

/** Snapshot of the full designer graph used for undo/dirty tracking. */
export interface DesignerSnapshot {
  states: WorkflowStateDto[];
  transitions: WorkflowTransitionDto[];
  branches: WorkflowBranchDto[];
}

interface Params {
  definitionId: string;
  states: WorkflowStateDto[];
  transitions: WorkflowTransitionDto[];
  branches: WorkflowBranchDto[];
  setStates: Dispatch<SetStateAction<WorkflowStateDto[]>>;
  setTransitions: Dispatch<SetStateAction<WorkflowTransitionDto[]>>;
  setBranches: Dispatch<SetStateAction<WorkflowBranchDto[]>>;
  savedSnapshot: MutableRefObject<DesignerSnapshot>;
  positionMapRef: MutableRefObject<Map<string, { x: number; y: number }>>;
  setHasChanges: Dispatch<SetStateAction<boolean>>;
  setSelectedElement: Dispatch<SetStateAction<SelectedElement>>;
  setAddStateOpen: Dispatch<SetStateAction<boolean>>;
  setTransitionModalOpen: Dispatch<SetStateAction<boolean>>;
  setTransitionPreFill: Dispatch<SetStateAction<{ fromStateId: string; toStateId: string } | null>>;
  /** Requests the canvas to re-frame all nodes — called after auto-layout writes new coordinates. */
  requestFitView: () => void;
}

export function useVisualDesignerHandlers({
  definitionId,
  states,
  transitions,
  branches,
  setStates,
  setTransitions,
  setBranches,
  savedSnapshot,
  positionMapRef,
  setHasChanges,
  setSelectedElement,
  setAddStateOpen,
  setTransitionModalOpen,
  setTransitionPreFill,
  requestFitView,
}: Params) {
  const { t } = useTranslation();
  const saveMutation = useSaveDefinitionGraph(definitionId);

  // Transient in-canvas violation highlight, keyed by state *name* (the canonical code name the
  // backend graph validator reports against — see WorkflowGraphValidator). Set when a save fails
  // with graph violations; cleared at the start of the next save attempt, on undo, and when the
  // user edits an offending node (clearViolationsForStates). Never persisted — purely a UI cue
  // that complements the multi-line save-fail toast by pointing at the broken states on the canvas.
  const [violationsByStateName, setViolationsByStateName] = useState<Map<string, GraphViolation[]>>(
    () => new Map(),
  );

  // Drop the given state names from the highlight map (idempotent — missing keys are no-ops).
  // Accepts old+new names so a rename clears the highlight regardless of which name the violation
  // was filed under, without needing the stale-closure-prone `states` array in the caller's deps.
  const clearViolationsForStates = useCallback((...names: (string | null | undefined)[]) => {
    setViolationsByStateName((prev) => {
      if (prev.size === 0) return prev;
      const next = new Map(prev);
      let changed = false;
      for (const name of names) {
        if (name && next.delete(name)) changed = true;
      }
      return changed ? next : prev;
    });
  }, []);

  // Graph-aware auto-arrange: run a left→right dagre layout over states + transitions + branches,
  // then write the computed coordinates back into the state/branch DTOs so they (a) render
  // immediately and (b) persist through the normal save path (stateToInput/branchToInput carry
  // positionX/Y). positionMapRef is cleared first: it holds stale session-drag positions which
  // win over persisted coords in buildNodes — leaving them would let an old drag override the
  // freshly computed layout.
  const handleAutoLayout = useCallback(() => {
    const { statePositions, gatewayPositions } = computeDagreLayout(states, transitions, branches);
    positionMapRef.current.clear();
    setStates((prev) =>
      prev.map((s) => {
        const pos = statePositions.get(String(s.id));
        return pos ? { ...s, positionX: pos.x, positionY: pos.y } : s;
      }),
    );
    setBranches((prev) =>
      prev.map((b) => {
        const pos = gatewayPositions.get(String(b.id));
        return pos ? { ...b, positionX: pos.x, positionY: pos.y } : b;
      }),
    );
    setHasChanges(true);
    requestFitView(); // re-frame the canvas once the new coordinates have been written
  }, [states, transitions, branches, positionMapRef, setStates, setBranches, setHasChanges, requestFitView]);

  const handleUndo = useCallback(() => {
    positionMapRef.current.clear(); // reset to grid so undo restores fresh layout
    setStates(savedSnapshot.current.states);
    setTransitions(savedSnapshot.current.transitions);
    setBranches(savedSnapshot.current.branches);
    setHasChanges(false);
    setSelectedElement(null);
    setViolationsByStateName(new Map()); // undo reverts the offending edits → highlight no longer valid
  }, [positionMapRef, savedSnapshot, setStates, setTransitions, setBranches, setHasChanges, setSelectedElement, setViolationsByStateName]);

  const handleSave = useCallback(async () => {
    // Every save attempt starts with a clean canvas — clear any stale highlight from a prior
    // failure before re-validating, so the user never sees outdated error markers.
    setViolationsByStateName(new Map());
    // Merge live drag-positions from positionMapRef into state DTOs so BE persists them.
    // positionMapRef holds the latest committed positions from ReactFlow drag-end events.
    const statesWithPos = states.map((s) => {
      const pos = positionMapRef.current.get(s.id);
      return pos != null ? { ...s, positionX: pos.x, positionY: pos.y } : s;
    });
    const stateMap = new Map(statesWithPos.map((s) => [s.id, s.name]));
    try {
      await saveMutation.mutateAsync({
        states: statesWithPos.map(stateToInput),
        transitions: transitions.map((tr) => transitionToInput(tr, stateMap)),
        branches: branches.map((b) => branchToInput(b, stateMap)),
      });
      savedSnapshot.current = { states: statesWithPos, transitions, branches };
      setHasChanges(false);
      appMessage.success(t('workflow.designer.saved', 'Đã lưu sơ đồ'));
    } catch (err: unknown) {
      // normalizeError rejects with NormalizedApiError; surface structured graph violations when present.
      const violations =
        err && typeof err === 'object' && 'violations' in err
          ? (err as { violations?: GraphViolation[] }).violations
          : undefined;
      if (violations && violations.length > 0) {
        // AntD message collapses "\n" → wrap in a pre-line node so the bullet list renders multi-line.
        // Longer duration (8s) since a violation list needs more reading time than a one-liner.
        appMessage.error(
          createElement('div', { style: { whiteSpace: 'pre-line' } }, formatGraphViolations(violations, t)),
          8,
        );
        // Additive to the toast: group state-scoped violations by name so the canvas can highlight
        // the broken state nodes. Transition-level violations (null stateName) carry no canvas anchor
        // and are intentionally omitted here — they remain surfaced in the toast above.
        const byName = new Map<string, GraphViolation[]>();
        for (const v of violations) {
          if (!v.stateName) continue;
          const bucket = byName.get(v.stateName);
          if (bucket) bucket.push(v);
          else byName.set(v.stateName, [v]);
        }
        setViolationsByStateName(byName);
      } else {
        appMessage.error(t('common.error', 'Có lỗi xảy ra'));
      }
    }
  }, [states, transitions, branches, saveMutation, savedSnapshot, positionMapRef, setHasChanges, setViolationsByStateName, t]);

  const handleStateAdded = useCallback(
    (newState: WorkflowStateDto) => {
      setStates((prev) => [...prev, newState]);
      setHasChanges(true);
      setAddStateOpen(false);
    },
    [setStates, setHasChanges, setAddStateOpen],
  );

  const handleCreateTransition = useCallback(
    ({ fromStateId, toStateId }: { fromStateId: string; toStateId: string }) => {
      setTransitionPreFill({ fromStateId, toStateId });
      setTransitionModalOpen(true);
    },
    [setTransitionPreFill, setTransitionModalOpen],
  );

  // On modal save: generate temp UUID, push to transitions, mark dirty, close modal
  const handleTransitionSave = useCallback(
    (values: Omit<WorkflowTransitionDto, 'id'> & { id?: string }) => {
      const newTr = { ...values, id: values.id ?? crypto.randomUUID() } as WorkflowTransitionDto;
      setTransitions((prev) => [...prev, newTr]);
      setHasChanges(true);
      setTransitionModalOpen(false);
      setTransitionPreFill(null);
    },
    [setTransitions, setHasChanges, setTransitionModalOpen, setTransitionPreFill],
  );

  // Reconnect handler — fires for both cross-node reroute AND same-node handle
  // orientation. Persists fromStateId/toStateId + sourceHandle/targetHandle into the
  // transitions array so the next save sends them to BE.
  const handleReconnectTransition = useCallback(
    (
      transitionId: string,
      update: {
        fromStateId: string;
        toStateId: string;
        sourceHandle: string | null;
        targetHandle: string | null;
      },
    ) => {
      setTransitions((prev) =>
        prev.map((tr) =>
          tr.id === transitionId
            ? {
                ...tr,
                fromStateId: update.fromStateId,
                toStateId: update.toStateId,
                sourceHandle: update.sourceHandle,
                targetHandle: update.targetHandle,
              }
            : tr,
        ),
      );
      setHasChanges(true);
    },
    [setTransitions, setHasChanges],
  );

  return {
    saveMutation,
    handleAutoLayout,
    handleUndo,
    handleSave,
    handleStateAdded,
    handleCreateTransition,
    handleTransitionSave,
    handleReconnectTransition,
    violationsByStateName,
    clearViolationsForStates,
  };
}
