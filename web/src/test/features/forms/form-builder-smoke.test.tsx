// Smoke tests for Form Builder components
// TC-FE-BUILDER-001: FormBuilderPage renders without crashing
// TC-FE-BUILDER-002: FieldPalette renders 5 category groups
// TC-FE-BUILDER-003: FieldCanvas renders empty state when no fields
// TC-FE-BUILDER-004: FieldPropertiesPanel renders placeholder when no field selected
// TC-FE-BUILDER-005: FormPreviewModal renders without crashing
// TC-FE-BUILDER-006: FormBuilderToolbar renders publish button for Draft templates
// TC-FE-BUILDER-007: OptionsEditor renders add button
// TC-FE-BUILDER-008: ValidationRulesEditor renders validation section for Text fields

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

vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => vi.fn(),
  useParams: () => ({ id: 'test-template-id' }),
  useLocation: () => ({ pathname: '/' }),
  Link: ({ children }: { children: ReactNode }) => children,
}));

vi.mock('@/features/auth/use-auth', () => ({
  useAuth: () => ({
    user: { profile: { tenant_id: 'test-tenant', sub: 'test-user', name: 'Test User' } },
    isAuthenticated: true,
  }),
}));

// ──────────────────────────────────────────────────────────────────────────────
// Form API mocks
// ──────────────────────────────────────────────────────────────────────────────

const mockTemplate = {
  id: 'test-template-id',
  code: 'FORM-001',
  name: 'Test Form',
  nameVi: 'Biểu mẫu thử',
  status: 'Draft' as const,
  version: 1,
  fields: [],
  submissionsCount: 0,
  createdAt: '2024-01-01T00:00:00Z',
};

vi.mock('@/features/forms/form-api', () => ({
  useFormTemplate:       () => ({ data: mockTemplate, isLoading: false }),
  useFormTemplates:      () => ({ data: { items: [], totalCount: 0 }, isLoading: false }),
  useAddField:           () => ({ mutate: vi.fn(), isPending: false }),
  useBulkReorderFields:  () => ({ mutate: vi.fn(), isPending: false }),
  useDeactivateField:    () => ({ mutate: vi.fn(), isPending: false }),
  usePublishTemplate:    () => ({ mutate: vi.fn(), isPending: false }),
  useUpdateField:        () => ({ mutate: vi.fn(), isPending: false }),
}));

// ──────────────────────────────────────────────────────────────────────────────
// Component imports — after mocks
// ──────────────────────────────────────────────────────────────────────────────

import { FormBuilderPage } from '@/features/forms/builder/form-builder-page';
import { FieldPalette } from '@/features/forms/builder/field-palette';
import { FieldCanvas } from '@/features/forms/builder/field-canvas';
import { FieldPropertiesPanel } from '@/features/forms/builder/field-properties-panel';
import { FormPreviewModal } from '@/features/forms/builder/form-preview-modal';
import { FormBuilderToolbar } from '@/features/forms/builder/form-builder-toolbar';
import { OptionsEditor } from '@/features/forms/builder/options-editor';
import { ValidationRulesEditor } from '@/features/forms/builder/validation-rules-editor';

// ──────────────────────────────────────────────────────────────────────────────
// Utility
// ──────────────────────────────────────────────────────────────────────────────

function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-BUILDER-001: FormBuilderPage
// ──────────────────────────────────────────────────────────────────────────────

describe('FormBuilderPage — TC-FE-BUILDER-001', () => {
  it('renders without crashing', () => {
    const { container } = render(<FormBuilderPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });

  it('renders template name in toolbar', () => {
    render(<FormBuilderPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('Test Form')).toBeTruthy();
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-BUILDER-002: FieldPalette — 5 category groups
// ──────────────────────────────────────────────────────────────────────────────

describe('FieldPalette — TC-FE-BUILDER-002', () => {
  it('renders without crashing', () => {
    const { container } = render(<FieldPalette disabled={false} />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });

  it('renders 5 category group labels', () => {
    render(<FieldPalette disabled={false} />, { wrapper: makeWrapper() });
    expect(screen.getByText('forms.builder.category.basic')).toBeTruthy();
    expect(screen.getByText('forms.builder.category.reference')).toBeTruthy();
    expect(screen.getByText('forms.builder.category.complex')).toBeTruthy();
    expect(screen.getByText('forms.builder.category.computed')).toBeTruthy();
    expect(screen.getByText('forms.builder.category.layout')).toBeTruthy();
  });

  it('renders search input', () => {
    const { container } = render(<FieldPalette disabled={false} />, { wrapper: makeWrapper() });
    expect(container.querySelector('input')).toBeTruthy();
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-BUILDER-003: FieldCanvas — empty state
// ──────────────────────────────────────────────────────────────────────────────

describe('FieldCanvas — TC-FE-BUILDER-003', () => {
  it('renders without crashing', () => {
    const { container } = render(
      <FieldCanvas
        fields={[]}
        selectedFieldId={null}
        onSelectField={vi.fn()}
        onDeleteField={vi.fn()}
        disabled={false}
      />,
      { wrapper: makeWrapper() }
    );
    expect(container.firstChild).toBeTruthy();
  });

  it('renders empty state hint when no fields', () => {
    render(
      <FieldCanvas
        fields={[]}
        selectedFieldId={null}
        onSelectField={vi.fn()}
        onDeleteField={vi.fn()}
        disabled={false}
      />,
      { wrapper: makeWrapper() }
    );
    expect(screen.getByText('forms.builder.canvas.emptyHint')).toBeTruthy();
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-BUILDER-004: FieldPropertiesPanel — no selection
// ──────────────────────────────────────────────────────────────────────────────

describe('FieldPropertiesPanel — TC-FE-BUILDER-004', () => {
  it('renders placeholder text when no field selected', () => {
    render(
      <FieldPropertiesPanel
        field={null}
        templateId="test-id"
        allFields={[]}
        disabled={false}
        onSave={vi.fn()}
        onDelete={vi.fn()}
      />,
      { wrapper: makeWrapper() }
    );
    expect(screen.getByText('forms.builder.properties.emptyHint')).toBeTruthy();
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-BUILDER-005: FormPreviewModal
// ──────────────────────────────────────────────────────────────────────────────

describe('FormPreviewModal — TC-FE-BUILDER-005', () => {
  it('renders without crashing when open', () => {
    const { container } = render(
      <FormPreviewModal
        open={true}
        onClose={vi.fn()}
        fields={[]}
        templateName="Test Form"
      />,
      { wrapper: makeWrapper() }
    );
    expect(container).toBeTruthy();
  });

  it('does not render modal content when closed', () => {
    render(
      <FormPreviewModal
        open={false}
        onClose={vi.fn()}
        fields={[]}
        templateName="Test Form"
      />,
      { wrapper: makeWrapper() }
    );
    // Modal is closed — title should not be in DOM
    expect(screen.queryByText('forms.builder.preview.title')).toBeFalsy();
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-BUILDER-006: FormBuilderToolbar — publish button
// ──────────────────────────────────────────────────────────────────────────────

describe('FormBuilderToolbar — TC-FE-BUILDER-006', () => {
  it('renders without crashing', () => {
    const { container } = render(
      <FormBuilderToolbar
        template={mockTemplate}
        onPublish={vi.fn()}
        onPreview={vi.fn()}
        isPublishing={false}
      />,
      { wrapper: makeWrapper() }
    );
    expect(container.firstChild).toBeTruthy();
  });

  it('renders publish button for Draft template', () => {
    render(
      <FormBuilderToolbar
        template={mockTemplate}
        onPublish={vi.fn()}
        onPreview={vi.fn()}
        isPublishing={false}
      />,
      { wrapper: makeWrapper() }
    );
    expect(screen.getByText('page.forms.detail.publishBtn')).toBeTruthy();
  });

  it('renders preview button', () => {
    render(
      <FormBuilderToolbar
        template={mockTemplate}
        onPublish={vi.fn()}
        onPreview={vi.fn()}
        isPublishing={false}
      />,
      { wrapper: makeWrapper() }
    );
    expect(screen.getByText('forms.builder.toolbar.preview')).toBeTruthy();
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-BUILDER-007: OptionsEditor — add button
// ──────────────────────────────────────────────────────────────────────────────

describe('OptionsEditor — TC-FE-BUILDER-007', () => {
  it('renders add button when not disabled', () => {
    render(
      <Form>
        <OptionsEditor disabled={false} />
      </Form>,
      { wrapper: makeWrapper() }
    );
    expect(screen.getByText('forms.builder.options.addOption')).toBeTruthy();
  });

  it('renders options section title', () => {
    render(
      <Form>
        <OptionsEditor disabled={false} />
      </Form>,
      { wrapper: makeWrapper() }
    );
    expect(screen.getByText('forms.builder.properties.optionsSection')).toBeTruthy();
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-BUILDER-008: ValidationRulesEditor
// ──────────────────────────────────────────────────────────────────────────────

describe('ValidationRulesEditor — TC-FE-BUILDER-008', () => {
  it('renders validation section for Text fieldType', () => {
    render(
      <Form>
        <ValidationRulesEditor fieldType="Text" disabled={false} />
      </Form>,
      { wrapper: makeWrapper() }
    );
    expect(screen.getByText('forms.builder.properties.validationSection')).toBeTruthy();
  });

  it('renders nothing for Boolean fieldType (no validation rules)', () => {
    render(
      <Form>
        <ValidationRulesEditor fieldType="Boolean" disabled={false} />
      </Form>,
      { wrapper: makeWrapper() }
    );
    // Boolean has no validation rules — component returns null
    expect(screen.queryByText('forms.builder.properties.validationSection')).toBeFalsy();
  });
});
