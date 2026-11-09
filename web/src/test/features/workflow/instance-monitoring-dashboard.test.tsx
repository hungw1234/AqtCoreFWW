// TC-FE-WF-DASH-001: render with empty instances → no crash, shows count label
// TC-FE-WF-DASH-002: render with 3 instances (1 completed) → 3 rows; completed checkbox disabled
// TC-FE-WF-DASH-003: render with isLoading=true → Spin present
// TC-FE-WF-DASH-004: select-all checkbox → selects only active instances (not completed)
// TC-FE-WF-DASH-006: ARIA grid semantics — role/aria attrs present (Phase 02)
// TC-FE-WF-DASH-007: keyboard nav — roving tabIndex (Phase 02)
// TC-FE-WF-DASH-008: focus entry — onFocus sets focusedCell to (0,0) when null (Phase 03)

import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';

// Mock react-i18next — returns key as value for deterministic assertions
vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (key: string, opts?: Record<string, unknown>) => {
      if (opts && 'entityId' in opts) return `${key}:${opts.entityId}`;
      if (opts && 'count' in opts) return `${key}:${opts.count}`;
      if (opts && 'id' in opts) return `${key}:${opts.id}`;
      return key;
    },
  }),
}));

// Mock @tanstack/react-virtual — jsdom has no scroll dimensions, so we render all items directly.
// Each virtualRow: { index, start: index*40, key: index }
// scrollToIndex added: required by useWorkflowGridKeyboardNav hook wired in Phase 02.
vi.mock('@tanstack/react-virtual', () => ({
  useVirtualizer: ({ count }: { count: number }) => ({
    getVirtualItems: () =>
      Array.from({ length: count }, (_, i) => ({ index: i, start: i * 40, key: i })),
    getTotalSize: () => count * 40,
    measureElement: () => {},
    scrollToIndex: vi.fn(),
  }),
}));

// Stub WorkflowBatchActionToolbar — avoids pulling in React Query + batch API wiring
vi.mock('@/features/workflow/workflow-batch-action-toolbar', () => ({
  WorkflowBatchActionToolbar: ({
    selectedInstanceIds,
    onClearSelection,
  }: {
    selectedInstanceIds: string[];
    onClearSelection: () => void;
  }) =>
    createElement(
      'div',
      { 'data-testid': 'batch-toolbar', 'data-count': selectedInstanceIds.length },
      createElement('button', { onClick: onClearSelection }, 'clear'),
    ),
}));

import { InstanceMonitoringDashboard } from '@/features/workflow/instance-monitoring-dashboard';
// T5-16: dashboard now accepts WorkflowInstanceListItem (thinner projection) not WorkflowInstanceDto
import type { WorkflowInstanceListItem } from '@/features/workflow/workflow-types';

// Helper wrapper — no QueryClient needed (batch toolbar is mocked)
function makeWrapper() {
  return ({ children }: { children: ReactNode }) => createElement('div', {}, children);
}

// Shared test fixtures — thin list projection (T5-16)
const activeInstance1: WorkflowInstanceListItem = {
  id: 'inst-1',
  definitionId: 'def-1',
  entityType: 'Case',
  entityId: 'CASE-001',
  currentStateId: 'PendingReview',
  startedAt: '2024-01-01T09:00:00Z',
  completedAt: null,
  dueAt: '2024-12-31T00:00:00Z',
};

const activeInstance2: WorkflowInstanceListItem = {
  id: 'inst-2',
  definitionId: 'def-1',
  entityType: 'Form',
  entityId: 'FORM-002',
  currentStateId: 'Approved',
  startedAt: '2024-02-01T09:00:00Z',
  completedAt: null,
  dueAt: null,
};

const completedInstance: WorkflowInstanceListItem = {
  id: 'inst-3',
  definitionId: 'def-1',
  entityType: 'Case',
  entityId: 'CASE-003',
  currentStateId: 'Closed',
  startedAt: '2024-03-01T09:00:00Z',
  completedAt: '2024-03-15T12:00:00Z',
  dueAt: null,
};

// TC-FE-WF-DASH-001: empty instances
describe('InstanceMonitoringDashboard — TC-FE-WF-DASH-001: empty instances', () => {
  it('renders without crashing when instances is empty', () => {
    const { container } = render(
      <InstanceMonitoringDashboard instances={[]} />,
      { wrapper: makeWrapper() },
    );
    expect(container.firstChild).toBeTruthy();
  });

  it('shows loaded-of-total label', () => {
    render(<InstanceMonitoringDashboard instances={[]} />, { wrapper: makeWrapper() });
    // t('workflow.instance.loadedOfTotal', { loaded: 0, total: 0 })
    // Mock returns key as-is (opts has neither 'count', 'entityId', nor 'id')
    expect(screen.getByText('workflow.instance.loadedOfTotal')).toBeTruthy();
  });

  it('renders no data rows when instances is empty', () => {
    const { container } = render(
      <InstanceMonitoringDashboard instances={[]} />,
      { wrapper: makeWrapper() },
    );
    expect(container.querySelectorAll('[role="row"]:not([aria-rowindex="1"])').length).toBe(0);
  });
});

// TC-FE-WF-DASH-002: 3 instances (2 active, 1 completed)
describe('InstanceMonitoringDashboard — TC-FE-WF-DASH-002: 3 instances with 1 completed', () => {
  const instances = [activeInstance1, activeInstance2, completedInstance];

  it('renders 3 data rows', () => {
    const { container } = render(
      <InstanceMonitoringDashboard instances={instances} />,
      { wrapper: makeWrapper() },
    );
    expect(container.querySelectorAll('[role="row"]:not([aria-rowindex="1"])').length).toBe(3);
  });

  it('renders entity IDs in table rows', () => {
    render(<InstanceMonitoringDashboard instances={instances} />, { wrapper: makeWrapper() });
    expect(screen.getByText('CASE-001')).toBeTruthy();
    expect(screen.getByText('FORM-002')).toBeTruthy();
    expect(screen.getByText('CASE-003')).toBeTruthy();
  });

  it('completed row checkbox is disabled', () => {
    const { container } = render(
      <InstanceMonitoringDashboard instances={instances} />,
      { wrapper: makeWrapper() },
    );
    // The completed row (inst-3) renders a disabled antd Checkbox input
    const checkboxInputs = container.querySelectorAll('[role="row"]:not([aria-rowindex="1"]) input[type="checkbox"]');
    // 3 row checkboxes: inst-1 enabled, inst-2 enabled, inst-3 disabled
    expect(checkboxInputs.length).toBe(3);
    const disabledInputs = Array.from(checkboxInputs).filter(
      (el) => (el as HTMLInputElement).disabled,
    );
    expect(disabledInputs.length).toBe(1);
  });

  it('shows loaded-of-total label', () => {
    render(<InstanceMonitoringDashboard instances={instances} />, { wrapper: makeWrapper() });
    // t('workflow.instance.loadedOfTotal', { loaded: 3, total: 0 }) → key (no matching opt)
    expect(screen.getByText('workflow.instance.loadedOfTotal')).toBeTruthy();
  });
});

// TC-FE-WF-DASH-003: loading state
describe('InstanceMonitoringDashboard — TC-FE-WF-DASH-003: loading state', () => {
  it('shows Spin component when isLoading is true', () => {
    const { container } = render(
      <InstanceMonitoringDashboard instances={[]} isLoading={true} />,
      { wrapper: makeWrapper() },
    );
    // antd Spin renders with .ant-spin class
    expect(container.querySelector('.ant-spin')).toBeTruthy();
  });

  it('renders no data rows while loading', () => {
    const { container } = render(
      <InstanceMonitoringDashboard instances={[]} isLoading={true} />,
      { wrapper: makeWrapper() },
    );
    expect(container.querySelectorAll('[role="row"]:not([aria-rowindex="1"])').length).toBe(0);
  });
});

// TC-FE-WF-DASH-004: select-all covers loaded window (RT-08 — was "active only", now "all loaded")
// BREAKING CHANGE T5-16: select-all scope changed from "active instances only" to
// "all loaded instances in the current window". The tooltip communicates this to users.
// Previously: select-all with 2 active + 1 completed → count=2.
// Now:        select-all with 2 active + 1 completed → count=3 (all loaded).
describe('InstanceMonitoringDashboard — TC-FE-WF-DASH-004: select-all loaded window (RT-08)', () => {
  const instances = [activeInstance1, activeInstance2, completedInstance];

  it('select-all checkbox selects all 3 loaded instances (including completed)', () => {
    const { container } = render(
      <InstanceMonitoringDashboard instances={instances} />,
      { wrapper: makeWrapper() },
    );
    // Header select-all is the first checkbox in the component (outside data rows)
    const allCheckboxInputs = container.querySelectorAll('input[type="checkbox"]');
    const selectAllInput = allCheckboxInputs[0] as HTMLInputElement;
    fireEvent.click(selectAllInput);

    // After select-all, batch toolbar should show 3 selected (all loaded, not just active)
    const toolbar = container.querySelector('[data-testid="batch-toolbar"]');
    expect(toolbar?.getAttribute('data-count')).toBe('3');
  });

  it('deselect-all clears selection', () => {
    const { container } = render(
      <InstanceMonitoringDashboard instances={instances} />,
      { wrapper: makeWrapper() },
    );
    const allCheckboxInputs = container.querySelectorAll('input[type="checkbox"]');
    const selectAllInput = allCheckboxInputs[0] as HTMLInputElement;

    // Select all first, then deselect
    fireEvent.click(selectAllInput);
    fireEvent.click(selectAllInput);

    const toolbar = container.querySelector('[data-testid="batch-toolbar"]');
    expect(toolbar?.getAttribute('data-count')).toBe('0');
  });
});

// TC-FE-WF-DASH-005: selectedIds purged when instances refetch removes a previously-selected id
describe('InstanceMonitoringDashboard — TC-FE-WF-DASH-005: selectedIds purged on refetch', () => {
  it('removes stale selected id when instances refetch omits it', () => {
    const initialInstances = [activeInstance1, activeInstance2];
    const { container, rerender } = render(
      <InstanceMonitoringDashboard instances={initialInstances} />,
      { wrapper: makeWrapper() },
    );

    // Select inst-1 via its row checkbox (second checkbox — first is select-all)
    const allCheckboxInputs = container.querySelectorAll('input[type="checkbox"]');
    // allCheckboxInputs[0] = header select-all, [1] = inst-1 row, [2] = inst-2 row
    fireEvent.click(allCheckboxInputs[1] as HTMLInputElement);

    // Verify inst-1 is selected (toolbar data-count = 1)
    const toolbar = container.querySelector('[data-testid="batch-toolbar"]');
    expect(toolbar?.getAttribute('data-count')).toBe('1');

    // Simulate refetch: instances no longer contains inst-1 (e.g., it was deleted or filtered out)
    rerender(<InstanceMonitoringDashboard instances={[activeInstance2]} />);

    // useEffect purge should remove inst-1 from selectedIds since it's no longer in instances
    expect(toolbar?.getAttribute('data-count')).toBe('0');
  });
});

// TC-FE-WF-DASH-006: ARIA grid semantics (Phase 02)
describe('InstanceMonitoringDashboard — TC-FE-WF-DASH-006: ARIA grid semantics', () => {
  const instances = [activeInstance1, activeInstance2, completedInstance];

  it('renders a role="grid" wrapper element', () => {
    const { container } = render(
      <InstanceMonitoringDashboard instances={instances} />,
      { wrapper: makeWrapper() },
    );
    expect(container.querySelector('[role="grid"]')).toBeTruthy();
  });

  it('grid wrapper has aria-multiselectable="true"', () => {
    const { container } = render(
      <InstanceMonitoringDashboard instances={instances} />,
      { wrapper: makeWrapper() },
    );
    const grid = container.querySelector('[role="grid"]');
    expect(grid?.getAttribute('aria-multiselectable')).toBe('true');
  });

  it('grid has aria-rowcount = instances.length + 1 (header row included)', () => {
    const { container } = render(
      <InstanceMonitoringDashboard instances={instances} />,
      { wrapper: makeWrapper() },
    );
    const grid = container.querySelector('[role="grid"]');
    // 3 data instances + 1 header row = 4
    expect(grid?.getAttribute('aria-rowcount')).toBe('4');
  });

  it('grid has aria-colcount="7"', () => {
    const { container } = render(
      <InstanceMonitoringDashboard instances={instances} />,
      { wrapper: makeWrapper() },
    );
    const grid = container.querySelector('[role="grid"]');
    expect(grid?.getAttribute('aria-colcount')).toBe('7');
  });

  it('header row has role="row" and aria-rowindex="1"', () => {
    const { container } = render(
      <InstanceMonitoringDashboard instances={instances} />,
      { wrapper: makeWrapper() },
    );
    const headerRow = container.querySelector('[role="row"][aria-rowindex="1"]');
    expect(headerRow).toBeTruthy();
  });

  it('header row contains 7 role="columnheader" elements', () => {
    const { container } = render(
      <InstanceMonitoringDashboard instances={instances} />,
      { wrapper: makeWrapper() },
    );
    const headerRow = container.querySelector('[role="row"][aria-rowindex="1"]');
    const colHeaders = headerRow?.querySelectorAll('[role="columnheader"]');
    expect(colHeaders?.length).toBe(7);
  });

  it('column headers have sequential aria-colindex 1-7', () => {
    const { container } = render(
      <InstanceMonitoringDashboard instances={instances} />,
      { wrapper: makeWrapper() },
    );
    const headerRow = container.querySelector('[role="row"][aria-rowindex="1"]');
    const colHeaders = Array.from(headerRow?.querySelectorAll('[role="columnheader"]') ?? []);
    const indices = colHeaders.map((el) => el.getAttribute('aria-colindex'));
    expect(indices).toEqual(['1', '2', '3', '4', '5', '6', '7']);
  });

  it('data rows have role="row" and aria-rowindex starting at 2', () => {
    const { container } = render(
      <InstanceMonitoringDashboard instances={instances} />,
      { wrapper: makeWrapper() },
    );
    const dataRows = container.querySelectorAll('[role="row"]:not([aria-rowindex="1"])');
    expect(dataRows.length).toBe(3);
    // First data row index = 2 (1-based, header offset)
    expect(dataRows[0].getAttribute('aria-rowindex')).toBe('2');
    expect(dataRows[1].getAttribute('aria-rowindex')).toBe('3');
    expect(dataRows[2].getAttribute('aria-rowindex')).toBe('4');
  });

  it('unselected data rows have aria-selected="false"', () => {
    const { container } = render(
      <InstanceMonitoringDashboard instances={instances} />,
      { wrapper: makeWrapper() },
    );
    const dataRows = container.querySelectorAll('[role="row"]:not([aria-rowindex="1"])');
    expect(dataRows[0].getAttribute('aria-selected')).toBe('false');
  });

  it('selected data row has aria-selected="true" after clicking checkbox', () => {
    const { container } = render(
      <InstanceMonitoringDashboard instances={instances} />,
      { wrapper: makeWrapper() },
    );
    // Click inst-1 row checkbox (second checkbox overall, first is select-all)
    const allCheckboxInputs = container.querySelectorAll('input[type="checkbox"]');
    fireEvent.click(allCheckboxInputs[1] as HTMLInputElement);

    const firstDataRow = container.querySelector('[role="row"][aria-rowindex="2"]');
    expect(firstDataRow?.getAttribute('aria-selected')).toBe('true');
  });

  it('each data row contains 7 role="gridcell" elements', () => {
    const { container } = render(
      <InstanceMonitoringDashboard instances={instances} />,
      { wrapper: makeWrapper() },
    );
    const firstDataRow = container.querySelector('[role="row"][aria-rowindex="2"]');
    const cells = firstDataRow?.querySelectorAll('[role="gridcell"]');
    expect(cells?.length).toBe(7);
  });

  it('gridcells have sequential aria-colindex 1-7', () => {
    const { container } = render(
      <InstanceMonitoringDashboard instances={instances} />,
      { wrapper: makeWrapper() },
    );
    const firstDataRow = container.querySelector('[role="row"][aria-rowindex="2"]');
    const cells = Array.from(firstDataRow?.querySelectorAll('[role="gridcell"]') ?? []);
    const indices = cells.map((el) => el.getAttribute('aria-colindex'));
    expect(indices).toEqual(['1', '2', '3', '4', '5', '6', '7']);
  });

  it('gridcells have data-row and data-col attributes', () => {
    const { container } = render(
      <InstanceMonitoringDashboard instances={instances} />,
      { wrapper: makeWrapper() },
    );
    const firstDataRow = container.querySelector('[role="row"][aria-rowindex="2"]');
    const cells = Array.from(firstDataRow?.querySelectorAll('[role="gridcell"]') ?? []);
    // Row 0 (0-based), cols 0-6
    expect(cells[0].getAttribute('data-row')).toBe('0');
    expect(cells[0].getAttribute('data-col')).toBe('0');
    expect(cells[6].getAttribute('data-col')).toBe('6');
  });
});

// TC-FE-WF-DASH-007: roving tabIndex (Phase 02)
describe('InstanceMonitoringDashboard — TC-FE-WF-DASH-007: roving tabIndex', () => {
  const instances = [activeInstance1, activeInstance2];

  it('cell (row=0, col=0) has tabIndex=0 when focusedCell is null (initial state)', () => {
    const { container } = render(
      <InstanceMonitoringDashboard instances={instances} />,
      { wrapper: makeWrapper() },
    );
    // First data row, first cell (col=0): tabIndex=0 by getCellTabIndex default
    const cell00 = container.querySelector('[data-row="0"][data-col="0"]');
    expect(cell00).toBeTruthy();
    expect(cell00?.getAttribute('tabindex')).toBe('0');
  });

  it('other cells have tabIndex=-1 in initial state', () => {
    const { container } = render(
      <InstanceMonitoringDashboard instances={instances} />,
      { wrapper: makeWrapper() },
    );
    const cell01 = container.querySelector('[data-row="0"][data-col="1"]');
    expect(cell01?.getAttribute('tabindex')).toBe('-1');
    const cell10 = container.querySelector('[data-row="1"][data-col="0"]');
    expect(cell10?.getAttribute('tabindex')).toBe('-1');
  });
});

// TC-FE-WF-DASH-008: focus entry handler (Phase 03)
describe('InstanceMonitoringDashboard — TC-FE-WF-DASH-008: focus entry handler', () => {
  const instances = [activeInstance1, activeInstance2];

  it('onFocus on grid wrapper sets focusedCell to (0,0) — cell (0,0) gets tabIndex=0', () => {
    const { container } = render(
      <InstanceMonitoringDashboard instances={instances} />,
      { wrapper: makeWrapper() },
    );
    const grid = container.querySelector('[role="grid"]');
    expect(grid).toBeTruthy();
    // Simulate focus entering the grid from outside
    fireEvent.focus(grid as Element);
    // After focus, cell (0,0) should still have tabIndex=0 (was already default; state confirmed set)
    const cell00 = container.querySelector('[data-row="0"][data-col="0"]');
    expect(cell00?.getAttribute('tabindex')).toBe('0');
  });

  it('grid wrapper receives keyboard events — ArrowDown changes focusedCell', () => {
    const { container } = render(
      <InstanceMonitoringDashboard instances={instances} />,
      { wrapper: makeWrapper() },
    );
    const grid = container.querySelector('[role="grid"]') as HTMLElement;
    expect(grid).toBeTruthy();

    // Focus the grid first
    fireEvent.focus(grid);

    // Fire ArrowDown on the grid
    fireEvent.keyDown(grid, { key: 'ArrowDown' });

    // After ArrowDown from row=0: focusedCell = {row:1, col:0}
    // row=1 cell=0 should now have tabIndex=0; row=0 cell=0 should be -1
    const cell10 = container.querySelector('[data-row="1"][data-col="0"]');
    expect(cell10?.getAttribute('tabindex')).toBe('0');
    const cell00 = container.querySelector('[data-row="0"][data-col="0"]');
    expect(cell00?.getAttribute('tabindex')).toBe('-1');
  });
});

// TC-FE-WF-DASH-009: grid container aria-label contains instance count (Phase 04)
describe('InstanceMonitoringDashboard — TC-FE-WF-DASH-009: grid aria-label with count', () => {
  const instances = [activeInstance1, activeInstance2, completedInstance];

  it('grid container has aria-label containing instance count', () => {
    const { container } = render(
      <InstanceMonitoringDashboard instances={instances} />,
      { wrapper: makeWrapper() },
    );
    const grid = container.querySelector('[role="grid"]');
    // t('workflow.instance.gridAriaLabel', { count: 3 }) → 'workflow.instance.gridAriaLabel:3'
    const ariaLabel = grid?.getAttribute('aria-label') ?? '';
    expect(ariaLabel).toContain('workflow.instance.gridAriaLabel');
    expect(ariaLabel).toContain('3');
  });
});

// TC-FE-WF-DASH-010: first data row aria-label contains entityId (Phase 04)
describe('InstanceMonitoringDashboard — TC-FE-WF-DASH-010: row aria-label with entityId', () => {
  const instances = [activeInstance1, activeInstance2, completedInstance];

  it('first data row has aria-label containing the instance entityId', () => {
    const { container } = render(
      <InstanceMonitoringDashboard instances={instances} />,
      { wrapper: makeWrapper() },
    );
    const firstDataRow = container.querySelector('[role="row"][aria-rowindex="2"]');
    // t('workflow.instance.rowAriaLabel', { entityId: 'CASE-001' }) → 'workflow.instance.rowAriaLabel:CASE-001'
    const ariaLabel = firstDataRow?.getAttribute('aria-label') ?? '';
    expect(ariaLabel).toContain('workflow.instance.rowAriaLabel');
    expect(ariaLabel).toContain('CASE-001');
  });
});

// TC-FE-WF-DASH-011: Tab-in behavior — Option A decision locked as regression test (Phase 04)
// Verifies that Tab into grid lands focus at data cell (0,0), NOT the sticky header select-all checkbox.
// WAI-ARIA grid spec compliant: Tab-in → first interactive data cell; header is not in roving tabIndex.
// T5-17 decision 2026-04-14.
describe('InstanceMonitoringDashboard — TC-FE-WF-DASH-011: Tab-in lands on data cell not header checkbox', () => {
  const instances = [activeInstance1, activeInstance2, completedInstance];

  it('after grid onFocus, data cell (0,0) has tabIndex=0 (not the header checkbox)', () => {
    const { container } = render(
      <InstanceMonitoringDashboard instances={instances} />,
      { wrapper: makeWrapper() },
    );
    const grid = container.querySelector('[role="grid"]') as HTMLElement;
    // Simulate Tab-in: browser fires focus on the grid wrapper
    fireEvent.focus(grid);

    // Data cell (row=0, col=0) must be the roving tabIndex entry point
    const firstDataCell = container.querySelector('[data-row="0"][data-col="0"]');
    expect(firstDataCell?.getAttribute('tabindex')).toBe('0');

    // Header select-all checkbox must NOT be tabIndex=0 (mouse-only per Option A)
    // Header row has aria-rowindex=1; its checkbox input should be naturally tabbable
    // but is NOT part of the roving tabIndex managed by the keyboard nav hook.
    const headerRow = container.querySelector('[role="row"][aria-rowindex="1"]');
    const headerCheckboxInput = headerRow?.querySelector('input[type="checkbox"]') as HTMLInputElement | null;
    // The header checkbox has its own natural tabIndex (not managed by grid nav hook)
    // Crucially: the gridcell at data-row="0" data-col="0" owns the roving tabIndex=0
    expect(firstDataCell).toBeTruthy();
    expect(firstDataCell?.getAttribute('data-row')).toBe('0');
    expect(firstDataCell?.getAttribute('data-col')).toBe('0');
    // Header checkbox does not carry the hook-managed tabIndex=0
    expect(headerCheckboxInput?.getAttribute('tabindex')).not.toBe('0');
  });
});
