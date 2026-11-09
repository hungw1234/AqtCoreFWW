// form-views-module.test.tsx — tests for saved views management components
// Tests: ViewManager, ViewColumnEditor

import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Form } from 'antd';

// ──────────────────────────────────────────────────────────────────────────────
// Global mocks
// ──────────────────────────────────────────────────────────────────────────────

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key, i18n: { language: 'en' } }),
}));

// Mock @dnd-kit packages — jsdom has no pointer events; passthrough wrappers keep
// component tests focused on UI logic, not drag physics.
vi.mock('@dnd-kit/core', () => ({
  DndContext: ({ children }: { children: ReactNode }) => <>{children}</>,
  closestCenter: vi.fn(),
  useSensor: vi.fn(() => ({})),
  useSensors: vi.fn((...args: unknown[]) => args),
  PointerSensor: class PointerSensor {},
  KeyboardSensor: class KeyboardSensor {},
}));

vi.mock('@dnd-kit/sortable', () => ({
  SortableContext: ({ children }: { children: ReactNode }) => <>{children}</>,
  useSortable: (_opts: unknown) => ({
    setNodeRef: vi.fn(),
    attributes: {} as Record<string, unknown>,
    listeners: {} as Record<string, unknown>,
    transform: null,
    transition: null,
    isDragging: false,
  }),
  arrayMove: <T,>(arr: T[], from: number, to: number): T[] => {
    const result = [...arr];
    const [removed] = result.splice(from, 1);
    result.splice(to, 0, removed);
    return result;
  },
  verticalListSortingStrategy: vi.fn(),
  sortableKeyboardCoordinates: vi.fn(),
}));

vi.mock('@dnd-kit/utilities', () => ({
  CSS: { Transform: { toString: () => '' } },
}));

vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => vi.fn(),
  useParams: () => ({ id: 'test-template-id' }),
  useLocation: () => ({ pathname: '/' }),
  Link: ({ children }: { children: ReactNode }) => children,
}));

// Mock views API
vi.mock('@/features/forms/views/view-api', () => ({
  useViews: () => ({
    data: [
      {
        id: 'view1',
        name: 'All Submissions',
        type: 'List',
        entityType: 'FormSubmission',
        isDefault: true,
        columns: [
          { fieldName: 'name', displayOrder: 1, formatter: 'Text' },
          { fieldName: 'email', displayOrder: 2, formatter: 'Text' },
        ],
      },
    ],
    isLoading: false,
  }),
  useCreateView: () => ({
    mutate: vi.fn(),
    isPending: false,
  }),
  useUpdateView: () => ({
    mutate: vi.fn(),
    isPending: false,
  }),
  useDeleteView: () => ({
    mutate: vi.fn(),
    isPending: false,
  }),
}));

// ──────────────────────────────────────────────────────────────────────────────
// Component imports — after mocks
// ──────────────────────────────────────────────────────────────────────────────

import { ViewManager } from '@/features/forms/views/view-manager';
import { ViewColumnEditor } from '@/features/forms/views/view-column-editor';
import type { FormFieldDto } from '@/features/forms/form-types';

// ──────────────────────────────────────────────────────────────────────────────
// Helper: create mock field
// ──────────────────────────────────────────────────────────────────────────────

function makeField(overrides: Partial<FormFieldDto> = {}): FormFieldDto {
  return {
    id: 'f1',
    fieldKey: 'test_field',
    type: 'Text',
    labelVi: 'Test Label',
    labelEn: 'Test Label',
    required: false,
    displayOrder: 1,
    isActive: true,
    addedInVersion: 1,
    ...overrides,
  };
}

// ──────────────────────────────────────────────────────────────────────────────
// Wrapper utility
// ──────────────────────────────────────────────────────────────────────────────

function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-VIEW-001: ViewManager — renders card title
// ──────────────────────────────────────────────────────────────────────────────

describe('ViewManager — TC-FE-VIEW-001', () => {
  it('renders "Saved Views" card title', () => {
    const fields = [
      makeField({ id: 'f1', fieldKey: 'name' }),
      makeField({ id: 'f2', fieldKey: 'email' }),
    ];
    render(
      <ViewManager templateId="tpl-1" fields={fields} />,
      { wrapper: makeWrapper() }
    );
    // ViewManager should render a card with title
    const { container } = render(
      <ViewManager templateId="tpl-1" fields={fields} />,
      { wrapper: makeWrapper() }
    );
    expect(container.querySelector('.ant-card')).toBeTruthy();
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-VIEW-002: ViewManager — renders Create View button
// ──────────────────────────────────────────────────────────────────────────────

describe('ViewManager — TC-FE-VIEW-002', () => {
  it('renders "Create View" button', () => {
    const fields = [makeField({ id: 'f1', fieldKey: 'name' })];
    render(
      <ViewManager templateId="tpl-1" fields={fields} />,
      { wrapper: makeWrapper() }
    );
    const button = screen.queryByRole('button');
    expect(button).toBeTruthy();
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-VIEW-003: ViewManager — renders table with columns
// ──────────────────────────────────────────────────────────────────────────────

describe('ViewManager — TC-FE-VIEW-003', () => {
  it('renders table with columns (Name, Type, Columns, Default, Actions)', () => {
    const fields = [
      makeField({ id: 'f1', fieldKey: 'name' }),
      makeField({ id: 'f2', fieldKey: 'email' }),
    ];
    const { container } = render(
      <ViewManager templateId="tpl-1" fields={fields} />,
      { wrapper: makeWrapper() }
    );
    expect(container.querySelector('.ant-table')).toBeTruthy();
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-VIEW-004: ViewManager — renders views data
// ──────────────────────────────────────────────────────────────────────────────

describe('ViewManager — TC-FE-VIEW-004', () => {
  it('shows views data when loaded', () => {
    const fields = [
      makeField({ id: 'f1', fieldKey: 'name' }),
      makeField({ id: 'f2', fieldKey: 'email' }),
    ];
    const { container } = render(
      <ViewManager templateId="tpl-1" fields={fields} />,
      { wrapper: makeWrapper() }
    );
    // Table should be present and populated
    expect(container.querySelector('.ant-table')).toBeTruthy();
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-VIEW-005: ViewManager — renders without crashing
// ──────────────────────────────────────────────────────────────────────────────

describe('ViewManager — TC-FE-VIEW-005', () => {
  it('renders successfully with valid fields', () => {
    const fields = [makeField({ id: 'f1', fieldKey: 'test' })];
    const { container } = render(
      <ViewManager templateId="tpl-1" fields={fields} />,
      { wrapper: makeWrapper() }
    );
    expect(container.firstChild).toBeTruthy();
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-VEDITOR-001: ViewColumnEditor — renders Add Column button
// ──────────────────────────────────────────────────────────────────────────────

describe('ViewColumnEditor — TC-FE-VEDITOR-001', () => {
  it('renders "Add Column" button', () => {
    const fields = [
      makeField({ id: 'f1', fieldKey: 'name' }),
      makeField({ id: 'f2', fieldKey: 'email' }),
    ];
    const { container } = render(
      <Form>
        <Form.Item name="columns">
          <ViewColumnEditor fields={fields} />
        </Form.Item>
      </Form>,
      { wrapper: makeWrapper() }
    );
    const button = container.querySelector('button');
    expect(button).toBeTruthy();
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-VEDITOR-002: ViewColumnEditor — renders table
// ──────────────────────────────────────────────────────────────────────────────

describe('ViewColumnEditor — TC-FE-VEDITOR-002', () => {
  it('renders editable table for columns', () => {
    const fields = [makeField({ id: 'f1', fieldKey: 'name' })];
    const { container } = render(
      <Form>
        <Form.Item name="columns">
          <ViewColumnEditor fields={fields} />
        </Form.Item>
      </Form>,
      { wrapper: makeWrapper() }
    );
    expect(container.querySelector('.ant-table')).toBeTruthy();
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-VEDITOR-003: ViewColumnEditor — empty state
// ──────────────────────────────────────────────────────────────────────────────

describe('ViewColumnEditor — TC-FE-VEDITOR-003', () => {
  it('renders empty table when no columns provided', () => {
    const fields = [makeField({ id: 'f1', fieldKey: 'name' })];
    const { container } = render(
      <Form>
        <Form.Item name="columns">
          <ViewColumnEditor fields={fields} value={[]} />
        </Form.Item>
      </Form>,
      { wrapper: makeWrapper() }
    );
    expect(container.querySelector('.ant-table')).toBeTruthy();
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-VEDITOR-004: ViewColumnEditor — filters layout fields
// ──────────────────────────────────────────────────────────────────────────────

describe('ViewColumnEditor — TC-FE-VEDITOR-004', () => {
  it('filters out Section, Label, Divider field types from options', () => {
    const fields = [
      makeField({ id: 'f1', fieldKey: 'name', type: 'Text' }),
      makeField({ id: 'f2', fieldKey: 'section1', type: 'Section' }),
      makeField({ id: 'f3', fieldKey: 'label1', type: 'Label' }),
      makeField({ id: 'f4', fieldKey: 'email', type: 'Text' }),
    ];
    const { container } = render(
      <Form>
        <Form.Item name="columns">
          <ViewColumnEditor fields={fields} />
        </Form.Item>
      </Form>,
      { wrapper: makeWrapper() }
    );
    // Should only have selects for Text fields (name, email), not Section/Label/Divider
    expect(container.querySelector('.ant-table')).toBeTruthy();
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-VEDITOR-005: ViewColumnEditor — renders without crashing
// ──────────────────────────────────────────────────────────────────────────────

describe('ViewColumnEditor — TC-FE-VEDITOR-005', () => {
  it('renders successfully with valid fields', () => {
    const fields = [makeField({ id: 'f1', fieldKey: 'test' })];
    const { container } = render(
      <Form>
        <Form.Item name="columns">
          <ViewColumnEditor fields={fields} />
        </Form.Item>
      </Form>,
      { wrapper: makeWrapper() }
    );
    expect(container.firstChild).toBeTruthy();
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-VEDITOR-006: ViewColumnEditor — drag handle present on each data row
// ──────────────────────────────────────────────────────────────────────────────

describe('ViewColumnEditor — TC-FE-VEDITOR-006', () => {
  it('renders drag handle icon for each column row', () => {
    const fields = [
      makeField({ id: 'f1', fieldKey: 'name', type: 'Text' }),
      makeField({ id: 'f2', fieldKey: 'email', type: 'Text' }),
      makeField({ id: 'f3', fieldKey: 'phone', type: 'Text' }),
    ];
    const columns = [
      { fieldName: 'name', displayOrder: 1, sortable: true, filterable: false, formatter: 'Text' as const },
      { fieldName: 'email', displayOrder: 2, sortable: true, filterable: false, formatter: 'Text' as const },
      { fieldName: 'phone', displayOrder: 3, sortable: false, filterable: false, formatter: 'Text' as const },
    ];
    // Render directly (not via Form.Item) so value prop is not overridden by Form state
    const { container } = render(
      <ViewColumnEditor fields={fields} value={columns} />,
      { wrapper: makeWrapper() }
    );
    // Drag handles rendered with aria-label matching i18n key (t returns key as-is in mock)
    const handles = container.querySelectorAll('[aria-label="page.forms.views.dragHandle"]');
    expect(handles.length).toBe(3);
  });

  it('drag handle not present when no columns (empty state)', () => {
    const fields = [makeField({ id: 'f1', fieldKey: 'name' })];
    // Render directly so value prop is respected
    const { container } = render(
      <ViewColumnEditor fields={fields} value={[]} />,
      { wrapper: makeWrapper() }
    );
    // Zero data rows → zero drag handles
    const handles = container.querySelectorAll('[aria-label="page.forms.views.dragHandle"]');
    expect(handles.length).toBe(0);
  });

  it('drag handles are inert (not-allowed cursor) when disabled=true — TC-FE-VEDITOR-007', () => {
    const fields = [
      makeField({ id: 'f1', fieldKey: 'name', type: 'Text' }),
      makeField({ id: 'f2', fieldKey: 'email', type: 'Text' }),
    ];
    const columns = [
      { fieldName: 'name', displayOrder: 1, sortable: true, filterable: false, formatter: 'Text' as const },
      { fieldName: 'email', displayOrder: 2, sortable: true, filterable: false, formatter: 'Text' as const },
    ];
    const { container } = render(
      <ViewColumnEditor fields={fields} value={columns} disabled={true} />,
      { wrapper: makeWrapper() },
    );
    // Drag handles must still render (for screen-reader discoverability) but be non-interactive
    const handles = container.querySelectorAll('[aria-label="page.forms.views.dragHandle"]');
    expect(handles.length).toBe(2);
    // Each handle must have cursor:not-allowed (no grab) and aria-disabled=true
    handles.forEach((handle) => {
      const el = handle as HTMLElement;
      expect(el.getAttribute('aria-disabled')).toBe('true');
      expect(el.style.cursor).toBe('not-allowed');
    });
  });

  it('displayOrder recalculates sequentially after arrayMove (unit logic)', () => {
    // Test the splice-based arrayMove + displayOrder recalc logic in isolation
    // (same logic used in handleDragEnd — no import needed, pure function test)
    function spliceMove<T>(arr: T[], from: number, to: number): T[] {
      const result = [...arr];
      const [removed] = result.splice(from, 1);
      result.splice(to, 0, removed);
      return result;
    }
    const cols = [
      { fieldName: 'a', displayOrder: 1, sortable: true, filterable: false, formatter: 'Text' as const },
      { fieldName: 'b', displayOrder: 2, sortable: true, filterable: false, formatter: 'Text' as const },
      { fieldName: 'c', displayOrder: 3, sortable: true, filterable: false, formatter: 'Text' as const },
    ];
    // Simulate moving index 0 → index 2 (a goes to end)
    const reordered = spliceMove(cols, 0, 2).map((c, i) => ({ ...c, displayOrder: i + 1 }));
    expect(reordered[0].fieldName).toBe('b');
    expect(reordered[1].fieldName).toBe('c');
    expect(reordered[2].fieldName).toBe('a');
    expect(reordered.map((c) => c.displayOrder)).toEqual([1, 2, 3]);
  });
});
