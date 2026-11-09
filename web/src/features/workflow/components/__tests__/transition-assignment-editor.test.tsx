// Plan 260523-1241 Phase 06c — TransitionAssignmentEditor — Collapse + Form.List + 3 clause modes.
//
// Controlled-component contract: { value?: AssignmentInput | null; onChange: (next) => void }.
// Wired via parent <Form.Item name="assignment"> in properties-sidebar.
//
// Facts FE-A1..FE-A5 per phase-06 doc:
//   FE-A1: null value → Collapse closed; expand surfaces Add Clause button.
//   FE-A2: Add ByPermission clause → PermCode multi-select rendered.
//   FE-A3: Initiator clause → no refs editor (RefsJson stays null on emit).
//   FE-A4: SpecificUser clause → WorkflowUserMultiSelect rendered.
//   FE-A5: Toggle AllowAssignorOverride → onChange fires with checked=true.

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { createElement } from 'react';

// Mock 06b infra so editor tests don't pull network/orval/collaboration.
vi.mock('../../hooks/use-perm-codes', () => ({
  useWorkflowPermCodes: () => ({
    codes: ['WORKFLOW.TASK.PROCESS', 'WORKFLOW.DEF.MANAGE'],
    isLoading: false,
    isError: false,
  }),
}));
vi.mock('../workflow-user-multi-select', () => ({
  WorkflowUserMultiSelect: (props: { value: string[]; placeholder?: string }) =>
    createElement('div', { 'data-testid': 'user-multi-select', 'data-value-count': props.value.length }, props.placeholder ?? 'user-picker'),
}));
vi.mock('../workflow-role-multi-select', () => ({
  WorkflowRoleMultiSelect: (props: { value: string[]; placeholder?: string }) =>
    createElement('div', { 'data-testid': 'role-multi-select', 'data-value-count': props.value.length }, props.placeholder ?? 'role-picker'),
}));
vi.mock('../workflow-group-multi-select', () => ({
  WorkflowGroupMultiSelect: (props: { value: string[]; placeholder?: string }) =>
    createElement('div', { 'data-testid': 'group-multi-select', 'data-value-count': props.value.length }, props.placeholder ?? 'group-picker'),
}));

import { TransitionAssignmentEditor } from '../transition-assignment-editor';
import type { AssignmentInput } from '../../workflow-types';

type OnChangeFn = (next: AssignmentInput | null) => void;

const wrapper = ({ children }: { children: ReactNode }) => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return createElement(QueryClientProvider, { client }, children);
};

describe('TransitionAssignmentEditor', () => {
  let onChange: ReturnType<typeof vi.fn<OnChangeFn>>;

  beforeEach(() => {
    onChange = vi.fn<OnChangeFn>();
  });

  // ── FE-A1 ────────────────────────────────────────────────────────────────
  it('FE-A1: null value → renders collapsed by default; expanding surfaces Add Clause', () => {
    render(<TransitionAssignmentEditor value={null} onChange={onChange} />, { wrapper });

    // Collapse header is present even when collapsed.
    const header = screen.getByText(/phân quyền/i);
    expect(header).toBeInTheDocument();

    // Expand by clicking the header.
    fireEvent.click(header);

    expect(screen.getByRole('button', { name: /thêm clause|add clause/i })).toBeInTheDocument();
  });

  // ── FE-A2 ────────────────────────────────────────────────────────────────
  it('FE-A2: Add ByPermission clause → PermCode multi-select rendered', () => {
    const value: AssignmentInput = {
      combineMode: 'Union',
      allowAssignorOverride: false,
      clauses: [{ mode: 'ByPermission', refsJson: null }],
    };
    render(<TransitionAssignmentEditor value={value} onChange={onChange} />, { wrapper });

    expect(screen.getByTestId('clause-row-0-perm-codes')).toBeInTheDocument();
  });

  // ── FE-A3 ────────────────────────────────────────────────────────────────
  it('FE-A3: Initiator clause → no refs editor visible (RefsJson stays null)', () => {
    const value: AssignmentInput = {
      combineMode: 'Union',
      allowAssignorOverride: false,
      clauses: [{ mode: 'Initiator', refsJson: null }],
    };
    render(<TransitionAssignmentEditor value={value} onChange={onChange} />, { wrapper });

    expect(screen.queryByTestId('clause-row-0-perm-codes')).not.toBeInTheDocument();
    expect(screen.queryByTestId('clause-row-0-user-picker')).not.toBeInTheDocument();
  });

  // ── FE-A3b — RT2-15 Phase 01 (2026-05-24): PreviousHandler clause ──────
  it('FE-A3b: PreviousHandler clause → no refs editor (auto-resolved at runtime)', () => {
    const value: AssignmentInput = {
      combineMode: 'Union',
      allowAssignorOverride: false,
      clauses: [{ mode: 'PreviousHandler', refsJson: null }],
    };
    render(<TransitionAssignmentEditor value={value} onChange={onChange} />, { wrapper });

    expect(screen.queryByTestId('clause-row-0-perm-codes')).not.toBeInTheDocument();
    expect(screen.queryByTestId('clause-row-0-user-picker')).not.toBeInTheDocument();
  });

  // ── FE-A4 ────────────────────────────────────────────────────────────────
  it('FE-A4: SpecificUser clause → WorkflowUserMultiSelect rendered', () => {
    const value: AssignmentInput = {
      combineMode: 'Union',
      allowAssignorOverride: false,
      clauses: [{ mode: 'SpecificUser', refsJson: null }],
    };
    render(<TransitionAssignmentEditor value={value} onChange={onChange} />, { wrapper });

    expect(screen.getByTestId('clause-row-0-user-picker')).toBeInTheDocument();
  });

  // ── FE-A4b — RT2-15 Phase 03 (2026-05-24): ByRole clause renders role picker ──
  it('FE-A4b: ByRole clause → WorkflowRoleMultiSelect rendered', () => {
    const value: AssignmentInput = {
      combineMode: 'Union',
      allowAssignorOverride: false,
      clauses: [{ mode: 'ByRole', refsJson: null }],
    };
    render(<TransitionAssignmentEditor value={value} onChange={onChange} />, { wrapper });

    expect(screen.getByTestId('clause-row-0-role-picker')).toBeInTheDocument();
    // Anti-tautology: ByRole picker rendered EXCLUSIVELY — not perm-codes nor user picker.
    expect(screen.queryByTestId('clause-row-0-perm-codes')).not.toBeInTheDocument();
    expect(screen.queryByTestId('clause-row-0-user-picker')).not.toBeInTheDocument();
  });

  // ── FE-A4c — RT2-15 Phase 02 (2026-05-25): ByGroup clause renders group picker ──
  it('FE-A4c: ByGroup clause → WorkflowGroupMultiSelect rendered exclusively', () => {
    const value: AssignmentInput = {
      combineMode: 'Union',
      allowAssignorOverride: false,
      clauses: [{ mode: 'ByGroup', refsJson: null }],
    };
    render(<TransitionAssignmentEditor value={value} onChange={onChange} />, { wrapper });

    expect(screen.getByTestId('clause-row-0-group-picker')).toBeInTheDocument();
    // Anti-tautology: group picker rendered EXCLUSIVELY — not perm-codes, role, or user picker.
    expect(screen.queryByTestId('clause-row-0-perm-codes')).not.toBeInTheDocument();
    expect(screen.queryByTestId('clause-row-0-role-picker')).not.toBeInTheDocument();
    expect(screen.queryByTestId('clause-row-0-user-picker')).not.toBeInTheDocument();
  });

  // ── FE-A6 — branch-child reuse: allowedModes restricts the clause-type dropdown ──
  it('FE-A6: allowedModes restricts the clause-mode dropdown to the given subset', () => {
    const value: AssignmentInput = {
      combineMode: 'Union',
      allowAssignorOverride: false,
      clauses: [{ mode: 'ByPermission', refsJson: null }],
    };
    render(
      <TransitionAssignmentEditor value={value} onChange={onChange} allowedModes={['ByPermission', 'ByRole']} />,
      { wrapper }
    );
    // First combobox = the clause-mode Select (the ByPermission picker is the second).
    const modeSelect = screen.getAllByRole('combobox')[0];
    fireEvent.mouseDown(modeSelect);
    // Exactly the 2 allowed modes — not the full set of 7.
    expect(screen.getAllByRole('option')).toHaveLength(2);
  });

  // ── FE-A7 — addClause must seed a SELECTABLE default under allowedModes ──
  it('FE-A7: addClause under allowedModes seeds a valid default mode (not the global Initiator)', () => {
    const value: AssignmentInput = {
      combineMode: 'Union',
      allowAssignorOverride: false,
      clauses: [],
    };
    render(
      <TransitionAssignmentEditor value={value} onChange={onChange} allowedModes={['ByPermission', 'ByRole']} />,
      { wrapper }
    );
    // Expand the collapsed panel, then add a clause.
    fireEvent.click(screen.getByText(/phân quyền/i));
    fireEvent.click(screen.getByRole('button', { name: /thêm clause|add clause/i }));

    const lastCall = onChange.mock.calls.at(-1)?.[0];
    expect(lastCall?.clauses[0].mode).toBe('ByPermission');
    expect(lastCall?.clauses[0].mode).not.toBe('Initiator');
  });

  // ── FE-A5 ────────────────────────────────────────────────────────────────
  it('FE-A5: Toggle AllowAssignorOverride → onChange emits with checked=true', () => {
    const value: AssignmentInput = {
      combineMode: 'Union',
      allowAssignorOverride: false,
      clauses: [{ mode: 'Initiator', refsJson: null }],
    };
    const { container } = render(
      <TransitionAssignmentEditor value={value} onChange={onChange} />,
      { wrapper }
    );

    const checkbox = within(container).getByRole('checkbox', { name: /override|cho phép/i });
    fireEvent.click(checkbox);

    expect(onChange).toHaveBeenCalled();
    const lastCall = onChange.mock.calls.at(-1)?.[0];
    expect(lastCall?.allowAssignorOverride).toBe(true);
  });
});
