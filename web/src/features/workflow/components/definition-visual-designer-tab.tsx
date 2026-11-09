// Visual Designer tab — wires ReactFlowCanvas, DesignerToolbar, PropertiesSidebar.
// Handlers (save/undo/layout/add/connect) → useVisualDesignerHandlers; selection inline.
import { useState, useCallback, useRef, useEffect } from 'react';
import type { WorkflowDefinitionDto, WorkflowStateDto, WorkflowTransitionDto, WorkflowBranchDto } from '../workflow-types';
import { makeGwId, parseBranchIdFromGwId } from './gateway-node-build-utils';
import { createForkBranch } from './designer-tab-fork-helpers';
import { ReactFlowCanvas } from './react-flow-canvas';
import { DesignerToolbar } from './designer-toolbar';
import { PropertiesSidebar } from './properties-sidebar';
import type { SelectedElement } from './properties-sidebar';
import { AddStateModal } from './add-state-modal';
import { TransitionEditorModal } from './transition-editor-modal';
import { useVisualDesignerHandlers } from './use-visual-designer-handlers';

interface Props {
  definition: WorkflowDefinitionDto;
}

export function DefinitionVisualDesignerTab({ definition }: Props) {
  // Local graph state — starts from definition data
  const [states, setStates] = useState<WorkflowStateDto[]>(definition.states);
  const [transitions, setTransitions] = useState<WorkflowTransitionDto[]>(definition.transitions);
  const [branches, setBranches] = useState<WorkflowBranchDto[]>(definition.branches ?? []);
  // Snapshot for undo — reset to last saved/initial state
  const savedSnapshot = useRef({
    states: definition.states,
    transitions: definition.transitions,
    branches: definition.branches ?? [],
  });
  const [hasChanges, setHasChanges] = useState(false);

  // Sync from prop on external change (refetch/save). Skip during in-flight edits.
  useEffect(() => {
    if (hasChanges) return;
    setStates(definition.states);
    setTransitions(definition.transitions);
    setBranches(definition.branches ?? []);
    savedSnapshot.current = {
      states: definition.states,
      transitions: definition.transitions,
      branches: definition.branches ?? [],
    };
  }, [definition.states, definition.transitions, definition.branches, hasChanges]);

  // In-memory position map — keeps dragged positions alive through refetch/save (tab session only)
  const positionMapRef = useRef<Map<string, { x: number; y: number }>>(new Map());

  // Monotonic fitView trigger — bumped after an auto-layout so the canvas re-frames the rearranged
  // graph (fitView lives inside the ReactFlowProvider, so it can't be called from this parent directly).
  const [fitViewSignal, setFitViewSignal] = useState(0);
  const requestFitView = useCallback(() => setFitViewSignal((n) => n + 1), []);

  const [selectedElement, setSelectedElement] = useState<SelectedElement>(null);
  const [addStateOpen, setAddStateOpen] = useState(false);

  // Draw-to-connect: pending pre-fill from canvas handle drag
  const [transitionModalOpen, setTransitionModalOpen] = useState(false);
  const [transitionPreFill, setTransitionPreFill] = useState<
    { fromStateId: string; toStateId: string } | null
  >(null);

  const {
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
  } = useVisualDesignerHandlers({
    definitionId: definition.id,
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
  });

  // ─── Reactive handlers — kept inline: depend on current states/transitions arrays ─
  const handleGraphChange = useCallback(() => setHasChanges(true), []);

  const handlePositionCommit = useCallback(
    (id: string, pos: { x: number; y: number }) => { positionMapRef.current.set(id, pos); }, []);

  const handleGatewayPositionCommit = useCallback((branchId: string, pos: { x: number; y: number }) => {
    positionMapRef.current.set(makeGwId(branchId), pos);
    setBranches((prev) => prev.map((b) => (b.id === branchId ? { ...b, positionX: pos.x, positionY: pos.y } : b)));
    setHasChanges(true);
  }, []);

  const handleNodeSelect = useCallback((nodeId: string | null) => {
    if (!nodeId) { setSelectedElement(null); return; }
    const branchId = parseBranchIdFromGwId(nodeId);
    if (branchId !== undefined) {
      const found = branches.find((b) => b.id === branchId);
      if (found) setSelectedElement({ type: 'gateway', data: found });
      return;
    }
    const found = states.find((s) => s.id === nodeId);
    if (found) setSelectedElement({ type: 'node', data: found });
  }, [states, branches]);

  const handleEdgeSelect = useCallback(
    (transitionId: string | null) => {
      if (!transitionId) { setSelectedElement(null); return; }
      const found = transitions.find((tr) => tr.id === transitionId);
      if (found) setSelectedElement({ type: 'edge', data: found });
    },
    [transitions],
  );

  const handleElementUpdate = useCallback((updated: SelectedElement) => {
    if (!updated) return;
    if (updated.type === 'node') {
      // Clear violation highlight: drop by old name + new name (rename-safe).
      const prevName = states.find((s) => s.id === updated.data.id)?.name;
      clearViolationsForStates(prevName, updated.data.name);
      setStates((prev) => prev.map((s) => (s.id === updated.data.id ? updated.data : s)));
    } else if (updated.type === 'gateway') {
      setBranches((prev) => prev.map((b) => (b.id === updated.data.id ? updated.data : b)));
    } else {
      setTransitions((prev) => prev.map((tr) => (tr.id === updated.data.id ? updated.data : tr)));
    }
    setHasChanges(true);
    setSelectedElement(updated);
  }, [states, clearViolationsForStates]);

  // Fork affordance is per-node (canvas fork button) — author a branch on the explicit state id.
  // Forkability (non-final FE UX + RB3 one-fork-per-state) is enforced upstream: buildNodes only
  // renders the button on forkable states, so handleAddFork trusts the id it receives.
  const handleAddFork = useCallback((stateId: string) => {
    const newBranch = createForkBranch(stateId, states);
    setBranches((prev) => [...prev, newBranch]);
    setSelectedElement({ type: 'gateway', data: newBranch });
    setHasChanges(true);
  }, [states]);

  return (
    <div style={{ position: 'relative' }}>
      <DesignerToolbar
        onAddState={() => setAddStateOpen(true)}
        onAutoLayout={handleAutoLayout}
        onSave={handleSave}
        onUndo={handleUndo}
        hasChanges={hasChanges}
        saving={saveMutation.isPending}
      />

      <ReactFlowCanvas
        states={states}
        transitions={transitions}
        branches={branches}
        onGraphChange={handleGraphChange}
        onNodeSelect={handleNodeSelect}
        onEdgeSelect={handleEdgeSelect}
        positionMap={positionMapRef.current}
        onPositionCommit={handlePositionCommit}
        onGatewayPositionCommit={handleGatewayPositionCommit}
        onCreateTransition={handleCreateTransition}
        onReconnectTransition={handleReconnectTransition}
        violationsByStateName={violationsByStateName}
        onAddFork={handleAddFork}
        fitViewSignal={fitViewSignal}
      />

      <PropertiesSidebar
        selectedElement={selectedElement}
        onUpdate={handleElementUpdate}
        onClose={() => setSelectedElement(null)}
        states={states}
        branches={branches}
      />

      <AddStateModal
        open={addStateOpen}
        nextSortOrder={states.length}
        onAdd={handleStateAdded}
        onCancel={() => setAddStateOpen(false)}
      />

      {/* Draw-to-connect: pre-filled fromStateId/toStateId; other fields left empty */}
      <TransitionEditorModal
        open={transitionModalOpen}
        initialFields={transitionPreFill ?? undefined}
        states={states}
        onSave={handleTransitionSave}
        onCancel={() => { setTransitionModalOpen(false); setTransitionPreFill(null); }}
      />
    </div>
  );
}
