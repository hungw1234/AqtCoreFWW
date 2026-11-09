// In-canvas graph-validation highlight after a failed designer save.
//
// Two surfaces under test:
//   1. buildNodes annotation (pure function): joins violations onto nodes by state *name*
//      (NOT id, NOT displayNameVi) and sets data.hasViolation + data.violations.
//   2. CustomStateNode rendering: shows the error badge + tooltip-bearing markup when a node's
//      data carries a violation, and stays clean when it does not.
//
// The name-based mapping is the load-bearing invariant: the backend graph validator reports
// violations against the canonical state code name, while react-flow nodes are keyed by state id.

import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import type { GraphViolation } from '@/core/api/api-error';
import type { WorkflowStateDto } from '@/features/workflow/workflow-types';
import { buildNodes } from '@/features/workflow/components/react-flow-canvas';
import type { StateNodeData } from '@/features/workflow/components/react-flow-canvas';

// ─── @xyflow/react mock — CustomStateNode imports Handle/Position ──────────────
vi.mock('@xyflow/react', () => ({
  Handle: () => null,
  Position: { Top: 'top', Right: 'right', Bottom: 'bottom', Left: 'left' },
}));

import { CustomStateNode } from '@/features/workflow/components/custom-state-node';

// ─── Fixtures ─────────────────────────────────────────────────────────────────

// Deliberately make id, name, and displayNameVi all DIFFERENT so the mapping test proves the
// join is on `name` (a future refactor switching to id or display name would fail these cases).
function makeState(overrides: Partial<WorkflowStateDto> = {}): WorkflowStateDto {
  return {
    id: 'node-id-1',
    name: 'DRAFT',
    displayNameVi: 'Bản nháp',
    displayNameEn: 'Draft',
    isInitial: false,
    isFinal: false,
    color: '#3366cc',
    sortOrder: 0,
    ...overrides,
  };
}

function violation(stateName: string, rule = 'R2', detail = 'no assignment'): GraphViolation {
  return { rule, stateName, transitionActionName: null, detail, metadata: null };
}

function dataOf(node: ReturnType<typeof buildNodes>[number]): StateNodeData {
  return node.data as unknown as StateNodeData;
}

// ─── buildNodes annotation ──────────────────────────────────────────────────

describe('buildNodes — violation annotation by state name', () => {
  it('flags a node whose name is in the violation map', () => {
    const states = [makeState({ id: 'n1', name: 'DRAFT' })];
    const map = new Map([['DRAFT', [violation('DRAFT')]]]);
    const nodes = buildNodes(states, new Map(), map);

    expect(dataOf(nodes[0]).hasViolation).toBe(true);
    expect(dataOf(nodes[0]).violations).toHaveLength(1);
    expect(dataOf(nodes[0]).violations?.[0].rule).toBe('R2');
  });

  it('does NOT flag a node whose name is absent from the map', () => {
    const states = [makeState({ id: 'n1', name: 'APPROVED' })];
    const map = new Map([['DRAFT', [violation('DRAFT')]]]);
    const nodes = buildNodes(states, new Map(), map);

    expect(dataOf(nodes[0]).hasViolation).toBe(false);
    expect(dataOf(nodes[0]).violations).toBeNull();
  });

  it('treats an undefined map as no violations for every node', () => {
    const states = [makeState({ name: 'DRAFT' }), makeState({ id: 'n2', name: 'APPROVED' })];
    const nodes = buildNodes(states, new Map(), undefined);

    nodes.forEach((n) => {
      expect(dataOf(n).hasViolation).toBe(false);
      expect(dataOf(n).violations).toBeNull();
    });
  });

  it('maps on name — NOT id and NOT displayNameVi (the load-bearing join invariant)', () => {
    // id, name, displayNameVi all distinct. Violation keyed by NAME only.
    const states = [makeState({ id: 'node-id-1', name: 'DRAFT', displayNameVi: 'Bản nháp' })];

    // keyed by name → flagged
    expect(dataOf(buildNodes(states, new Map(), new Map([['DRAFT', [violation('DRAFT')]]]))[0]).hasViolation).toBe(true);
    // keyed by id → NOT flagged (proves we don't join on id)
    expect(dataOf(buildNodes(states, new Map(), new Map([['node-id-1', [violation('node-id-1')]]]))[0]).hasViolation).toBe(false);
    // keyed by displayNameVi → NOT flagged (proves we don't join on display name)
    expect(dataOf(buildNodes(states, new Map(), new Map([['Bản nháp', [violation('Bản nháp')]]]))[0]).hasViolation).toBe(false);
  });

  it('does not crash when the map names a state not on the canvas', () => {
    const states = [makeState({ name: 'DRAFT' })];
    const map = new Map([['GHOST_STATE', [violation('GHOST_STATE')]]]);
    const nodes = buildNodes(states, new Map(), map);

    expect(nodes).toHaveLength(1);
    expect(dataOf(nodes[0]).hasViolation).toBe(false);
  });

  it('attaches every violation for a state with multiple', () => {
    const states = [makeState({ name: 'DRAFT' })];
    const map = new Map([['DRAFT', [violation('DRAFT', 'R1'), violation('DRAFT', 'R2')]]]);
    const nodes = buildNodes(states, new Map(), map);

    expect(dataOf(nodes[0]).violations).toHaveLength(2);
  });
});

// ─── CustomStateNode rendering ──────────────────────────────────────────────

describe('CustomStateNode — error visual', () => {
  // @xyflow/react NodeProps marks these as required; the component ignores them.
  const stubNodeProps = {
    zIndex: 0,
    draggable: false,
    selectable: false,
    deletable: false,
    dragging: false,
    isConnectable: false,
    positionAbsoluteX: 0,
    positionAbsoluteY: 0,
  } as const;

  function renderNode(data: StateNodeData) {
    return render(
      <CustomStateNode
        id="n1"
        type="workflowState"
        data={data as unknown as Record<string, unknown>}
        selected={false}
        {...stubNodeProps}
      />,
    );
  }

  it('renders the error badge when data.hasViolation is true', () => {
    renderNode({ ...makeState({ name: 'DRAFT' }), hasViolation: true, violations: [violation('DRAFT')] });
    expect(screen.getByLabelText('Lỗi xác thực')).toBeTruthy();
  });

  it('does not render the error badge when there is no violation', () => {
    renderNode({ ...makeState({ name: 'DRAFT' }), hasViolation: false, violations: null });
    expect(screen.queryByLabelText('Lỗi xác thực')).toBeNull();
  });

  it('marks the card aria-invalid only when flagged', () => {
    const { container, rerender } = renderNode({
      ...makeState({ name: 'DRAFT' }),
      hasViolation: true,
      violations: [violation('DRAFT')],
    });
    expect(container.querySelector('[aria-invalid="true"]')).toBeTruthy();

    rerender(
      <CustomStateNode
        id="n1"
        type="workflowState"
        data={{ ...makeState({ name: 'DRAFT' }), hasViolation: false, violations: null } as unknown as Record<string, unknown>}
        selected={false}
        {...stubNodeProps}
      />,
    );
    expect(container.querySelector('[aria-invalid="true"]')).toBeNull();
  });
});
