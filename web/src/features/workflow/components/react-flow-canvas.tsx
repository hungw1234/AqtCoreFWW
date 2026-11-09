// React Flow canvas for workflow designer — wraps ReactFlowProvider + ReactFlow
// Converts WorkflowStateDto[] + WorkflowTransitionDto[] to nodes/edges
// Supports drag-reposition, connect (draw transitions), and node/edge selection
// Helpers + types live in react-flow-canvas-helpers.ts (LOC budget)

import '@xyflow/react/dist/style.css';

import { useCallback, useMemo, useEffect, useRef } from 'react';
import {
  ReactFlow,
  ReactFlowProvider,
  Controls,
  Background,
  MiniMap,
  useNodesState,
  useEdgesState,
  useReactFlow,
  addEdge,
  reconnectEdge,
} from '@xyflow/react';
import type { Connection, NodeMouseHandler, EdgeMouseHandler } from '@xyflow/react';
import {
  NODE_TYPES,
  EDGE_TYPES,
  buildNodes,
  buildEdges,
  type ReactFlowCanvasProps,
} from './react-flow-canvas-helpers';
import { buildGatewayNodes, buildGatewayEdges, parseBranchIdFromGwId } from './gateway-node-build-utils';
import { useReactFlowRevealGuard } from './use-reactflow-reveal-guard';

// Re-export pure helpers/types — tests import buildNodes from here
export { buildNodes, buildEdges } from './react-flow-canvas-helpers';
export type { ReactFlowCanvasProps, GraphChange, StateNodeData } from './react-flow-canvas-helpers';

// Inner canvas — must be inside ReactFlowProvider
function WorkflowFlowInner({
  states,
  transitions,
  onGraphChange,
  onNodeSelect,
  onEdgeSelect,
  positionMap,
  onPositionCommit,
  onCreateTransition,
  onReconnectTransition,
  violationsByStateName,
  branches,
  onGatewayPositionCommit,
  onAddFork,
  fitViewSignal,
}: ReactFlowCanvasProps) {
  const { fitView } = useReactFlow();
  // Fork affordance options — memoized so the Set ref is stable across renders (only changes when
  // branches change). buildNodes reads this to flag forkable nodes + inject the stable onAddFork ref.
  // Undefined when onAddFork isn't wired (read-only canvases) → no fork buttons rendered.
  const forkOptions = useMemo(
    () =>
      onAddFork
        ? { branchedStateIds: new Set((branches ?? []).map((b) => String(b.branchingStateId))), onAddFork }
        : undefined,
    [branches, onAddFork],
  );

  const initialNodes = useMemo(
    () => [
      ...buildNodes(states, positionMap, violationsByStateName, forkOptions),
      ...buildGatewayNodes(branches ?? [], states, positionMap),
    ],
    [states, positionMap, violationsByStateName, branches, forkOptions],
  );
  const initialEdges = useMemo(
    () => [...buildEdges(transitions, states), ...buildGatewayEdges(branches ?? [], states)],
    [transitions, states, branches],
  );

  const [nodes, setNodes, onNodesChange] = useNodesState(initialNodes);
  const [edges, setEdges, onEdgesChange] = useEdgesState(initialEdges);

  // Defensive: the designer renders today only because its antd Tabs container is
  // pre-sized + visible at mount (ResizeObserver-timing luck), NOT structural immunity —
  // it shares the same latent react-flow v12 reveal defect as the BP read-only canvas
  // (verified at source 2026-05-18). The guard removes that fragility so future layout
  // refactors don't silently regress this surface to a blank canvas. refitOnRearm:false —
  // after the initial reveal the user owns the viewport (pan/zoom/drag); an external
  // save/undo re-sync must repair measurement WITHOUT discarding their manual view.
  useReactFlowRevealGuard(nodes, { refitOnRearm: false });

  // True only for the next nodes/edges commit caused by a user interaction (drag/connect/
  // reconnect/select) — distinguishes those from the prop re-sync below so we don't echo an
  // externally-driven change back to the parent. Reset by the notify effect after it reads it.
  const userDrivenChange = useRef(false);
  const hasMounted = useRef(false);

  // Re-sync when external states/transitions change (after save/undo). These are NOT user
  // edits, so leave userDrivenChange untouched (defensively clear any stale flag).
  // violationsByStateName is in the deps so a save-fail (which swaps in a new violation Map)
  // re-runs buildNodes and toggles the error highlight WITHOUT requiring a states change.
  // buildNodes reads positionMap first, so this re-sync never clobbers session drag positions.
  useEffect(() => {
    userDrivenChange.current = false;
    setNodes([
      ...buildNodes(states, positionMap, violationsByStateName, forkOptions),
      ...buildGatewayNodes(branches ?? [], states, positionMap),
    ]);
  }, [states, setNodes, positionMap, violationsByStateName, branches, forkOptions]);
  useEffect(() => {
    userDrivenChange.current = false;
    setEdges([...buildEdges(transitions, states), ...buildGatewayEdges(branches ?? [], states)]);
  }, [transitions, states, setEdges, branches]);

  // Re-frame the graph after an auto-layout. The parent bumps fitViewSignal AFTER it has written the
  // new dagre coordinates into the state/branch DTOs; those flow through the re-sync effects above,
  // so we defer fitView one animation frame to let the repositioned nodes commit + measure before
  // framing them. The initial signal value is skipped — mount framing is owned by the `fitView` prop
  // and the reveal guard, and refitting here on mount would fight a user's restored viewport.
  const fitViewMounted = useRef(false);
  useEffect(() => {
    if (!fitViewMounted.current) { fitViewMounted.current = true; return; }
    const raf = requestAnimationFrame(() => fitView({ padding: 0.25, duration: 300 }));
    return () => cancelAnimationFrame(raf);
  }, [fitViewSignal, fitView]);

  // Notify parent of graph changes for save/undo tracking.
  const notifyChange = useCallback(
    (ns: typeof nodes, es: typeof edges) => onGraphChange(ns as ReturnType<typeof buildNodes>, es as ReturnType<typeof buildEdges>),
    [onGraphChange],
  );

  // Side effect runs AFTER commit (not inside a state updater — React 18 StrictMode would
  // double-invoke an updater body). Only fires for user-driven changes.
  useEffect(() => {
    if (!hasMounted.current) { hasMounted.current = true; return; }
    if (!userDrivenChange.current) return;
    userDrivenChange.current = false;
    notifyChange(nodes, edges);
  }, [nodes, edges, notifyChange]);

  const handleNodesChange: typeof onNodesChange = useCallback(
    (changes) => {
      userDrivenChange.current = true;
      onNodesChange(changes);
      // Commit final position to parent map on drag-end (fires once per drag release)
      changes.forEach((c) => {
        if (c.type === 'position' && c.dragging === false && c.position) {
          const branchId = parseBranchIdFromGwId(c.id);
          if (branchId) {
            onGatewayPositionCommit?.(branchId, c.position);
          } else {
            onPositionCommit?.(c.id, c.position);
          }
        }
      });
    },
    [onNodesChange, onPositionCommit, onGatewayPositionCommit],
  );

  const handleEdgesChange: typeof onEdgesChange = useCallback(
    (changes) => {
      userDrivenChange.current = true;
      onEdgesChange(changes);
    },
    [onEdgesChange],
  );

  // Draw new transition by connecting handles.
  // When onCreateTransition is provided, delegate to parent (opens TransitionEditorModal).
  // Otherwise fall back to local addEdge — preserves backward compatibility + test fallback path.
  const handleConnect = useCallback(
    (connection: Connection) => {
      if (onCreateTransition) {
        onCreateTransition({ fromStateId: connection.source!, toStateId: connection.target! });
        return;
      }
      userDrivenChange.current = true;
      // Cast preserves the literal `type` discriminator on returned edges (matches handleReconnect).
      setEdges((eds) => addEdge({ ...connection, type: 'workflowTransition', data: {} }, eds) as typeof eds);
    },
    [onCreateTransition, setEdges],
  );

  // Reconnect handler — fires when user drags an existing edge's endpoint to a different
  // handle. Always propagates to parent so BOTH cross-node reroute AND same-node handle
  // orientation get persisted (sourceHandle/targetHandle on transitions).
  const handleReconnect = useCallback(
    (oldEdge: Parameters<typeof reconnectEdge>[0], newConnection: Connection) => {
      userDrivenChange.current = true;
      // reconnectEdge widens type to EdgeBase[]; cast back to preserve our literal `type` discriminator
      setEdges((eds) => reconnectEdge(oldEdge, newConnection, eds) as typeof eds);
      if (onReconnectTransition && newConnection.source && newConnection.target) {
        onReconnectTransition(oldEdge.id, {
          fromStateId: newConnection.source,
          toStateId: newConnection.target,
          sourceHandle: newConnection.sourceHandle ?? null,
          targetHandle: newConnection.targetHandle ?? null,
        });
      }
      // The edges-state change above triggers the notify effect with the up-to-date arrays.
    },
    [setEdges, onReconnectTransition],
  );

  const handleNodeClick: NodeMouseHandler = useCallback(
    (_e, node) => onNodeSelect(node.id),
    [onNodeSelect],
  );

  const handleEdgeClick: EdgeMouseHandler = useCallback(
    (_e, edge) => onEdgeSelect(edge.id),
    [onEdgeSelect],
  );

  const handlePaneClick = useCallback(() => {
    onNodeSelect(null);
    onEdgeSelect(null);
  }, [onNodeSelect, onEdgeSelect]);

  const showMiniMap = states.length > 8;

  return (
    <div style={{ width: '100%', height: 520 }}>
      <ReactFlow
        nodes={nodes}
        edges={edges}
        nodeTypes={NODE_TYPES}
        edgeTypes={EDGE_TYPES}
        onNodesChange={handleNodesChange}
        onEdgesChange={handleEdgesChange}
        onConnect={handleConnect}
        onReconnect={handleReconnect}
        edgesReconnectable
        reconnectRadius={20}
        onNodeClick={handleNodeClick}
        onEdgeClick={handleEdgeClick}
        onPaneClick={handlePaneClick}
        fitView
        fitViewOptions={{ padding: 0.25 }}
        minZoom={0.3}
        deleteKeyCode="Delete"
      >
        <Controls />
        <Background gap={16} />
        {showMiniMap && <MiniMap nodeStrokeWidth={3} />}
      </ReactFlow>
    </div>
  );
}

// ReactFlowCanvas — exported canvas wrapped in required ReactFlowProvider
export function ReactFlowCanvas(props: ReactFlowCanvasProps) {
  return (
    <ReactFlowProvider>
      <WorkflowFlowInner {...props} />
    </ReactFlowProvider>
  );
}
