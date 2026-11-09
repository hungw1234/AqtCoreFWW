// TC-FE-WF-R7: Persist drag positions — buildNodes position resolution
// TC-FE-WF-R11: Draw-to-connect — handleConnect callback delegation
// Phase-01 + Phase-02 of T5-20 (workflow designer UX).
//
// R7 Cases (pure function, no rendering):
//   R7-A: empty positionMap → all nodes at grid positions
//   R7-B: fully-populated positionMap → all nodes use persisted positions
//   R7-C: partial positionMap → known ids use persisted, unknown ids fall back to grid
//
// R11 Cases (rendered component, mocked @xyflow/react):
//   R11-A: onCreateTransition provided → callback called with correct {fromStateId, toStateId}
//   R11-B: onCreateTransition provided → addEdge NOT called (parent owns edge lifecycle)
//   R11-C: onCreateTransition absent → addEdge fallback called (original behavior preserved)

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render } from '@testing-library/react';
import { buildNodes } from '@/features/workflow/components/react-flow-canvas';
import { buildEdges } from '@/features/workflow/components/react-flow-canvas-helpers';
import type { WorkflowStateDto, WorkflowTransitionDto } from '@/features/workflow/workflow-types';

// ─── @xyflow/react mock ────────────────────────────────────────────────────────
// vi.hoisted() runs before module imports — required for values used inside vi.mock() factory.
const { mockAddEdge, getOnConnect, setOnConnect } = vi.hoisted(() => {
  let _onConnect: ((connection: { source: string; target: string }) => void) | undefined;
  return {
    mockAddEdge: vi.fn((conn: unknown, eds: unknown[]) => [...eds, conn]),
    getOnConnect: () => _onConnect,
    setOnConnect: (fn: typeof _onConnect) => { _onConnect = fn; },
  };
});

vi.mock('@xyflow/react', () => ({
  ReactFlowProvider: ({ children }: { children: React.ReactNode }) => children,
  ReactFlow: (props: { onConnect?: (c: { source: string; target: string }) => void }) => {
    // Capture handler each render so tests can trigger it
    setOnConnect(props.onConnect);
    return null;
  },
  Controls: () => null,
  Background: () => null,
  MiniMap: () => null,
  useNodesState: (init: unknown[]) => [init, vi.fn(), vi.fn()],
  // setEdges must invoke updater fns so addEdge inside the updater actually runs
  useEdgesState: (init: unknown[]) => [
    init,
    vi.fn((updater: unknown) => { if (typeof updater === 'function') updater(init); }),
    vi.fn(),
  ],
  addEdge: mockAddEdge,
  // WorkflowFlowInner calls useReactFlow() for fitView (auto-layout re-frame). Stub it.
  useReactFlow: () => ({ fitView: vi.fn() }),
  // useReactFlowRevealGuard (added to WorkflowFlowInner) calls useStoreApi().getState().
  // Return nodesInitialized:true so the guard's first rAF tick early-returns — no poll,
  // deterministic, no jsdom layout needed.
  useStoreApi: () => ({
    getState: () => ({ nodesInitialized: true, updateNodeInternals: vi.fn() }),
  }),
}));

// Import AFTER vi.mock() declaration (vitest hoisting ensures mock is active)
import { ReactFlowCanvas } from '@/features/workflow/components/react-flow-canvas';

// ─── Fixtures ─────────────────────────────────────────────────────────────────

function makeState(id: string, idx: number): WorkflowStateDto {
  return {
    id,
    name: `state-${idx}`,
    displayNameVi: '',
    displayNameEn: '',
    isInitial: idx === 0,
    isFinal: false,
    color: '#ffffff',
    sortOrder: idx,
  };
}

const STATE_NODE_WIDTH = 180;
const STATE_NODE_HEIGHT = 80;
const HORIZONTAL_GAP = 60;
const COLS = 3;
const ROW_GAP = 60;

function gridPos(i: number) {
  return {
    x: (i % COLS) * (STATE_NODE_WIDTH + HORIZONTAL_GAP),
    y: Math.floor(i / COLS) * (STATE_NODE_HEIGHT + ROW_GAP),
  };
}

const states = [makeState('s1', 0), makeState('s2', 1), makeState('s3', 2)];

const minimalCanvasProps = {
  states,
  transitions: [] as WorkflowTransitionDto[],
  onGraphChange: vi.fn(),
  onNodeSelect: vi.fn(),
  onEdgeSelect: vi.fn(),
};

// ─── R7: buildNodes position resolution (pure function, no rendering) ─────────

// TC-FE-WF-R7-A: empty map → grid positions for all nodes
describe('buildNodes — TC-FE-WF-R7-A: empty positionMap falls back to grid', () => {
  it('assigns grid positions when positionMap is empty', () => {
    const nodes = buildNodes(states, new Map());
    expect(nodes).toHaveLength(3);
    nodes.forEach((n, i) => {
      expect(n.position).toEqual(gridPos(i));
    });
  });
});

// TC-FE-WF-R7-B: fully-populated map → persisted positions used for all nodes
describe('buildNodes — TC-FE-WF-R7-B: fully-populated positionMap returns persisted positions', () => {
  it('uses persisted positions from map for all known state ids', () => {
    const map = new Map([
      ['s1', { x: 100, y: 200 }],
      ['s2', { x: 300, y: 400 }],
      ['s3', { x: 500, y: 600 }],
    ]);
    const nodes = buildNodes(states, map);
    expect(nodes[0].position).toEqual({ x: 100, y: 200 });
    expect(nodes[1].position).toEqual({ x: 300, y: 400 });
    expect(nodes[2].position).toEqual({ x: 500, y: 600 });
  });
});

// TC-FE-WF-R7-C: partial map → persisted for known ids, grid fallback for unknowns
describe('buildNodes — TC-FE-WF-R7-C: partial positionMap mixes persisted and grid', () => {
  it('uses persisted position for known id and grid position for unknown id', () => {
    const map = new Map([['s1', { x: 42, y: 99 }]]);
    const nodes = buildNodes(states, map);
    // s1 — persisted
    expect(nodes[0].position).toEqual({ x: 42, y: 99 });
    // s2 — unknown → grid(1)
    expect(nodes[1].position).toEqual(gridPos(1));
    // s3 — unknown → grid(2)
    expect(nodes[2].position).toEqual(gridPos(2));
  });
});

// ─── R11: handleConnect callback delegation (rendered component) ───────────────

describe('ReactFlowCanvas — TC-FE-WF-R11: draw-to-connect delegation', () => {
  beforeEach(() => {
    // Reset captured handler and mock call history between cases
    setOnConnect(undefined);
    mockAddEdge.mockClear();
  });

  // TC-FE-WF-R11-A: onCreateTransition provided → called with correct payload
  it('R11-A: calls onCreateTransition with fromStateId and toStateId when provided', () => {
    const onCreateTransition = vi.fn();
    render(
      <ReactFlowCanvas
        {...minimalCanvasProps}
        onCreateTransition={onCreateTransition}
      />,
    );

    const onConnect = getOnConnect();
    expect(onConnect).toBeDefined();
    onConnect!({ source: 's1', target: 's2' });

    expect(onCreateTransition).toHaveBeenCalledOnce();
    expect(onCreateTransition).toHaveBeenCalledWith({ fromStateId: 's1', toStateId: 's2' });
  });

  // TC-FE-WF-R11-B: onCreateTransition provided → addEdge NOT called
  it('R11-B: does NOT call addEdge when onCreateTransition is provided', () => {
    const onCreateTransition = vi.fn();
    render(
      <ReactFlowCanvas
        {...minimalCanvasProps}
        onCreateTransition={onCreateTransition}
      />,
    );

    getOnConnect()!({ source: 's1', target: 's2' });

    expect(mockAddEdge).not.toHaveBeenCalled();
  });

  // TC-FE-WF-R11-C: onCreateTransition absent → addEdge fallback called
  it('R11-C: falls back to addEdge when onCreateTransition is not provided', () => {
    render(<ReactFlowCanvas {...minimalCanvasProps} />);

    getOnConnect()!({ source: 's1', target: 's2' });

    expect(mockAddEdge).toHaveBeenCalledOnce();
  });

  // MTS-6: notifyChange moved out of state-updater bodies into a post-commit effect with a
  // mount guard — the parent must NOT be told the graph changed just because it rendered.
  it('R11-D: does not invoke onGraphChange on initial mount', () => {
    const onGraphChange = vi.fn();
    render(<ReactFlowCanvas {...minimalCanvasProps} onGraphChange={onGraphChange} />);

    expect(onGraphChange).not.toHaveBeenCalled();
  });
});

// ─── BUG2-CLONE defense-in-depth: buildEdges drops transitions with unresolvable FK ───────────
// Regression for the WorkflowDefinition.Clone() FK=0 corruption: a transition whose
// fromStateId/toStateId is not a known state id must be dropped + warned, not handed to
// react-flow (which silently renders nothing — the original blank-visualization symptom).
// Pre-guard buildEdges had no `states` arg and no filter → these cases would NOT drop/warn.
describe('buildEdges — drops + warns transitions whose FK is not a known state', () => {
  function tr(id: number, from: unknown, to: unknown): WorkflowTransitionDto {
    return {
      id, fromStateId: from, toStateId: to, actionName: `act-${id}`,
    } as unknown as WorkflowTransitionDto;
  }

  it('keeps the resolvable transition, drops corrupt (FK=0 / unknown state) ones, warns each', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
    // `states` fixture ids are 's1','s2','s3'
    const edges = buildEdges([tr(1, 's1', 's2'), tr(2, 0, 0), tr(3, 's2', 's9')], states);

    expect(edges).toHaveLength(1);
    expect(edges[0].id).toBe(1);
    expect(warn).toHaveBeenCalledTimes(2);
    warn.mockRestore();
  });

  it('returns zero edges when every transition FK is corrupt (the WF#13 symptom)', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
    const edges = buildEdges([tr(21, 0, 0), tr(22, 0, 0)], states);

    expect(edges).toHaveLength(0);
    expect(warn).toHaveBeenCalledTimes(2);
    warn.mockRestore();
  });
});
