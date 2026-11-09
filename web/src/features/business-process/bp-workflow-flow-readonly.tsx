// Read-only workflow visualization for BP form-maps view.
// Reuses NODE_TYPES/EDGE_TYPES/buildNodes/buildEdges from the workflow designer infra so
// the rendered graph is visually consistent with the full Visual Designer tab. All edit
// interactions (drag/connect/reconnect/select/delete) are disabled — pan + zoom remain
// for navigation only.
//
// Deliberately NOT extending react-flow-canvas.tsx with a readOnly flag: that file owns
// the editing flow (user-driven change tracking, position commit, transition modal hook-
// ins). Two surfaces with sharply different concerns — keep them separate.

import '@xyflow/react/dist/style.css';

import { useMemo } from 'react';
import { ReactFlow, ReactFlowProvider, Controls, Background } from '@xyflow/react';
import type { WorkflowStateDto, WorkflowTransitionDto } from '@/features/workflow/workflow-types';
import {
  NODE_TYPES,
  EDGE_TYPES,
  buildNodes,
  buildEdges,
} from '@/features/workflow/components/react-flow-canvas-helpers';
import { useReactFlowRevealGuard } from '@/features/workflow/components/use-reactflow-reveal-guard';

interface Props {
  states: WorkflowStateDto[];
  transitions: WorkflowTransitionDto[];
  /** Outer container height in px. Defaults to 400 — fits comfortably alongside the
   *  edit panel without dominating the modal. */
  height?: number;
}

function BpWorkflowFlowReadOnlyInner({ states, transitions, height = 400 }: Props) {
  const nodes = useMemo(() => buildNodes(states), [states]);
  const edges = useMemo(() => buildEdges(transitions, states), [transitions, states]);

  // This canvas mounts inside a display:none ancestor (antd Card reveal in the form-maps
  // editor) where react-flow's per-node ResizeObserver never fires → blank canvas. The
  // shared reveal guard force-measures via the store action until nodesInitialized flips.
  // See use-reactflow-reveal-guard.ts for the full root-cause invariant.
  useReactFlowRevealGuard(nodes);

  return (
    <div style={{ width: '100%', height }}>
      <ReactFlow
        nodes={nodes}
        edges={edges}
        nodeTypes={NODE_TYPES}
        edgeTypes={EDGE_TYPES}
        nodesDraggable={false}
        nodesConnectable={false}
        elementsSelectable={false}
        edgesReconnectable={false}
        zoomOnScroll
        panOnDrag
        fitView
        fitViewOptions={{ padding: 0.2 }}
        minZoom={0.3}
        proOptions={{ hideAttribution: true }}
      >
        <Controls showInteractive={false} />
        <Background gap={16} />
      </ReactFlow>
    </div>
  );
}

export function BpWorkflowFlowReadOnly(props: Props) {
  return (
    <ReactFlowProvider>
      <BpWorkflowFlowReadOnlyInner {...props} />
    </ReactFlowProvider>
  );
}
