// Vitest unit tests for branch-editor.tsx
// Run explicitly: npx vitest run src/features/workflow/components/branch-editor.spec.tsx
// NOTE: vitest.config.ts excludes **/*.spec.ts but NOT **/*.spec.tsx — this file runs normally.

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { createElement } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import type { WorkflowBranchDto, WorkflowStateDto } from '../workflow-types';

// Mock user search so SingleUserSelect doesn't hit network
vi.mock('@/features/collaboration/user-search-api', () => ({
  useUserSearch: vi.fn().mockReturnValue({ data: [], isLoading: false }),
}));

// ChildRow now reuses TransitionAssignmentEditor, which calls useWorkflowPermCodes (orval/axios).
// Stub it so the branch tests stay network-free.
vi.mock('../hooks/use-perm-codes', () => ({
  useWorkflowPermCodes: () => ({ codes: [], isLoading: false, isError: false }),
}));

// Mock react-i18next so t() returns the key suffix as label
vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (_key: string, fallback: string) => fallback }),
}));

import { BranchEditor } from './branch-editor';

// ── fixtures ──────────────────────────────────────────────────────────────────

const stateA: WorkflowStateDto = {
  id: 's1', name: 'Draft', displayNameVi: 'Nháp', displayNameEn: 'Draft',
  isInitial: true, isFinal: false, color: '#aaa', sortOrder: 0,
};
const stateB: WorkflowStateDto = {
  id: 's2', name: 'Review', displayNameVi: 'Xem xét', displayNameEn: 'Review',
  isInitial: false, isFinal: false, color: '#bbb', sortOrder: 1,
};
const stateC: WorkflowStateDto = {
  id: 's3', name: 'Done', displayNameVi: 'Xong', displayNameEn: 'Done',
  isInitial: false, isFinal: true, color: '#ccc', sortOrder: 2,
};
const states = [stateA, stateB, stateC];

const baseBranch: WorkflowBranchDto = {
  id: 'b1',
  branchingStateId: 's1',
  continuationStateId: 's3',
  branchType: 'And',
  requiredCount: null,
  allowCancelOnAnyReject: false,
  timeoutMinutes: 0,
  positionX: null,
  positionY: null,
  children: [],
};

const wrapper = ({ children }: { children: ReactNode }) => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return createElement(QueryClientProvider, { client }, children);
};

// ── helpers ───────────────────────────────────────────────────────────────────

function renderEditor(
  branch: WorkflowBranchDto,
  onChange = vi.fn(),
  branchedStateIds?: Set<string>,
) {
  return render(
    <BranchEditor value={branch} states={states} onChange={onChange} branchedStateIds={branchedStateIds} />,
    { wrapper },
  );
}

// Open the branching-state Select (first combobox in DOM order) and return its option labels.
// rc-select renders the option label into aria-label (textContent is the raw value in test mode).
function openBranchingStateOptions(): string[] {
  const branchingSelect = screen.getAllByRole('combobox')[0];
  fireEvent.mouseDown(branchingSelect);
  return screen.getAllByRole('option').map((o) => o.getAttribute('aria-label') ?? '');
}

// ── tests ─────────────────────────────────────────────────────────────────────

describe('BranchEditor', () => {
  beforeEach(() => vi.clearAllMocks());

  it('renders branch type select', () => {
    renderEditor(baseBranch);
    // The branch type label is visible in the rendered form
    expect(screen.getByText(/Kiểu nhánh/i)).toBeInTheDocument();
  });

  it('does NOT show RequiredCount input when branchType is And', () => {
    renderEditor(baseBranch);
    expect(screen.queryByText(/Số lượng N cần duyệt/i)).not.toBeInTheDocument();
  });

  it('shows RequiredCount input when branchType is NOfM', () => {
    renderEditor({ ...baseBranch, branchType: 'NOfM', requiredCount: 2 });
    expect(screen.getByText(/Số lượng N cần duyệt/i)).toBeInTheDocument();
  });

  it('renders add-child button', () => {
    renderEditor(baseBranch);
    expect(screen.getByText(/Thêm nhánh con/i)).toBeInTheDocument();
  });

  it('calls onChange with new child when add-child clicked', () => {
    const onChange = vi.fn();
    renderEditor(baseBranch, onChange);
    fireEvent.click(screen.getByText(/Thêm nhánh con/i));
    expect(onChange).toHaveBeenCalledTimes(1);
    const emitted = onChange.mock.calls[0][0] as WorkflowBranchDto;
    expect(emitted.children).toHaveLength(1);
    expect(emitted.children[0].name).toBe('NewBranch');
    expect(emitted.children[0].displayNameVi).toBe('Nhánh mới');
  });

  it('calls onChange with empty children when only child is removed', () => {
    const onChange = vi.fn();
    const branchWithChild: WorkflowBranchDto = {
      ...baseBranch,
      children: [{
        id: 'c1', targetStateId: 's2', name: 'child1', displayNameVi: 'Nhánh 1',
        sortOrder: 0, resolutions: [],
      }],
    };
    renderEditor(branchWithChild, onChange);
    // AntD renders icon-only danger buttons with accessible name "delete" (from the icon label).
    // The child row header has a delete button; "Thêm kết quả" button is not dangerous.
    const deleteButtons = screen.getAllByRole('button', { name: 'delete' });
    // First delete button is the child-row remove button (rendered before add-resolution).
    fireEvent.click(deleteButtons[0]);
    expect(onChange).toHaveBeenCalledTimes(1);
    const emitted = onChange.mock.calls[0][0] as WorkflowBranchDto;
    expect(emitted.children).toHaveLength(0);
  });

  it('renders the per-child assignment (pool) editor for each child', () => {
    const branchWithChild: WorkflowBranchDto = {
      ...baseBranch,
      children: [{
        id: 'c1', targetStateId: 's2', name: 'child1', displayNameVi: 'Nhánh 1',
        sortOrder: 0, resolutions: [],
      }],
    };
    renderEditor(branchWithChild);
    // The reused TransitionAssignmentEditor renders its Collapse header even when collapsed.
    expect(screen.getByText(/Phân quyền/i)).toBeInTheDocument();
  });

  it('emits the authored pool on child.assignment when an assignment is set', () => {
    const onChange = vi.fn();
    const assignment = {
      combineMode: 'Union' as const,
      allowAssignorOverride: false,
      clauses: [{ mode: 'ByPermission' as const, refsJson: '["WORKFLOW.TASK.PROCESS"]' }],
    };
    const branchWithChild: WorkflowBranchDto = {
      ...baseBranch,
      children: [{
        id: 'c1', targetStateId: 's2', name: 'child1', displayNameVi: 'Nhánh 1',
        sortOrder: 0, resolutions: [], assignment,
      }],
    };
    renderEditor(branchWithChild, onChange);
    // Toggle the override checkbox inside the (already-expanded) assignment editor → onChange must
    // bubble a full child carrying the assignment, proving the editor is wired to child.assignment.
    fireEvent.click(screen.getByRole('checkbox', { name: /override|cho phép/i }));
    const emitted = onChange.mock.calls.at(-1)?.[0] as WorkflowBranchDto;
    expect(emitted.children[0].assignment?.allowAssignorOverride).toBe(true);
    expect(emitted.children[0].assignment?.clauses).toHaveLength(1);
  });

  it('emitted branch on add has a unique id (UUID-like)', () => {
    const onChange = vi.fn();
    renderEditor(baseBranch, onChange);
    fireEvent.click(screen.getByText(/Thêm nhánh con/i));
    const emitted = onChange.mock.calls[0][0] as WorkflowBranchDto;
    expect(emitted.children[0].id).toMatch(/^[0-9a-f-]{36}$/i);
  });

  it('renders continuation state label', () => {
    renderEditor(baseBranch);
    expect(screen.getByText(/Bước hội tụ/i)).toBeInTheDocument();
  });

  it('renders allowCancelOnAnyReject switch', () => {
    renderEditor(baseBranch);
    expect(screen.getByText(/Hủy khi có nhánh từ chối/i)).toBeInTheDocument();
  });

  it('renders branching state label', () => {
    renderEditor(baseBranch);
    expect(screen.getByText(/Bước rẽ nhánh/i)).toBeInTheDocument();
  });

  it('excludes final states from the branching-state picker (RB4)', () => {
    renderEditor(baseBranch);
    const labels = openBranchingStateOptions();
    expect(labels).toContain('Nháp');   // s1 (current, initial)
    expect(labels).toContain('Xem xét'); // s2 (non-final)
    expect(labels).not.toContain('Xong'); // s3 (final) — excluded
  });

  it('excludes states already owning another branch (RB3)', () => {
    renderEditor(baseBranch, vi.fn(), new Set(['s2']));
    const labels = openBranchingStateOptions();
    expect(labels).toContain('Nháp');     // s1 current — kept
    expect(labels).not.toContain('Xem xét'); // s2 owns another branch — excluded
    expect(labels).not.toContain('Xong');    // s3 final — excluded
  });

  it('keeps the current branching state selectable even if in branchedStateIds', () => {
    // s1 is the current branch's branching state AND flagged owned — must stay selectable.
    renderEditor(baseBranch, vi.fn(), new Set(['s1']));
    const labels = openBranchingStateOptions();
    expect(labels).toContain('Nháp');     // s1 current — kept via exception clause
    expect(labels).toContain('Xem xét');  // s2 — still offerable
  });

  it('emits onChange with the new branchingStateId when changed', () => {
    const onChange = vi.fn();
    renderEditor(baseBranch, onChange);
    const branchingSelect = screen.getAllByRole('combobox')[0];
    fireEvent.mouseDown(branchingSelect);
    // rc-select's clickable label node carries the visible text (the role=option div shows the
    // raw value in jsdom). Click by text so the selection handler fires.
    fireEvent.click(screen.getByText('Xem xét'));
    expect(onChange).toHaveBeenCalledTimes(1);
    const emitted = onChange.mock.calls[0][0] as WorkflowBranchDto;
    expect(emitted.branchingStateId).toBe('s2');
  });
});
