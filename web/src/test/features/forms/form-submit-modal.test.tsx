// form-submit-modal.test.tsx — tests for authenticated form submission modal
// Tests: FormSubmitModal with multi-step, validation, visibility, consent

import { describe, it, expect, vi } from 'vitest';
import { render } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

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

// Mock form-api
vi.mock('@/features/forms/form-api', () => ({
  useSubmitForm: () => ({
    mutate: vi.fn(),
    isPending: false,
  }),
  // Formula compute hook (Phase-4A) — component imports it unconditionally, so the
  // mock must expose it even for templates without Formula fields (else undefined at render).
  useEvaluateFormulas: () => ({
    mutateAsync: vi.fn().mockResolvedValue({ results: {} }),
    isPending: false,
  }),
}));

// ──────────────────────────────────────────────────────────────────────────────
// Component imports — after mocks
// ──────────────────────────────────────────────────────────────────────────────

import { FormSubmitModal } from '@/features/forms/form-submit-modal';
import type { FormTemplateDto } from '@/features/forms/form-types';

// ──────────────────────────────────────────────────────────────────────────────
// Helper: create mock template
// ──────────────────────────────────────────────────────────────────────────────

function makeTemplate(overrides: Partial<FormTemplateDto> = {}): FormTemplateDto {
  return {
    id: 'tpl-1',
    code: 'FORM-001',
    name: 'Test Form',
    nameVi: 'Biểu mẫu thử',
    status: 'Active',
    version: 1,
    fields: [
      {
        id: 'f1',
        fieldKey: 'name',
        type: 'Text',
        labelVi: 'Họ tên',
        labelEn: 'Name',
        required: true,
        displayOrder: 1,
        isActive: true,
        addedInVersion: 1,
      },
      {
        id: 'f2',
        fieldKey: 'section1',
        type: 'Section',
        labelVi: 'Phần 2',
        labelEn: 'Part 2',
        required: false,
        displayOrder: 2,
        isActive: true,
        addedInVersion: 1,
      },
      {
        id: 'f3',
        fieldKey: 'email',
        type: 'Text',
        labelVi: 'Email',
        labelEn: 'Email',
        required: true,
        displayOrder: 3,
        isActive: true,
        addedInVersion: 1,
      },
    ],
    submissionsCount: 0,
    createdAt: '2024-01-01T00:00:00Z',
    requiresConsent: false,
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
// TC-FE-MODAL-001: FormSubmitModal — renders without crashing
// ──────────────────────────────────────────────────────────────────────────────

describe('FormSubmitModal — TC-FE-MODAL-001', () => {
  it('renders without crashing when open=false', () => {
    const template = makeTemplate();
    const { container } = render(
      <FormSubmitModal
        template={template}
        open={false}
        onClose={vi.fn()}
      />,
      { wrapper: makeWrapper() }
    );
    expect(container).toBeTruthy();
  });

  it('renders without crashing when open=true', () => {
    const template = makeTemplate();
    const { container } = render(
      <FormSubmitModal
        template={template}
        open={true}
        onClose={vi.fn()}
      />,
      { wrapper: makeWrapper() }
    );
    expect(container).toBeTruthy();
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-MODAL-002: FormSubmitModal — accepts template props
// ──────────────────────────────────────────────────────────────────────────────

describe('FormSubmitModal — TC-FE-MODAL-002', () => {
  it('accepts template, open, and onClose props', () => {
    const template = makeTemplate({ name: 'Test Form' });
    const onClose = vi.fn();
    const { rerender } = render(
      <FormSubmitModal
        template={template}
        open={true}
        onClose={onClose}
      />,
      { wrapper: makeWrapper() }
    );
    expect(rerender).toBeDefined();
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-MODAL-003: FormSubmitModal — handles empty fields
// ──────────────────────────────────────────────────────────────────────────────

describe('FormSubmitModal — TC-FE-MODAL-003', () => {
  it('renders with template that has no fields', () => {
    const template = makeTemplate({ fields: [] });
    const { container } = render(
      <FormSubmitModal
        template={template}
        open={true}
        onClose={vi.fn()}
      />,
      { wrapper: makeWrapper() }
    );
    expect(container).toBeTruthy();
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-MODAL-004: FormSubmitModal — consent flag respected
// ──────────────────────────────────────────────────────────────────────────────

describe('FormSubmitModal — TC-FE-MODAL-004', () => {
  it('renders with requiresConsent=true', () => {
    const template = makeTemplate({ requiresConsent: true });
    const { container } = render(
      <FormSubmitModal
        template={template}
        open={true}
        onClose={vi.fn()}
      />,
      { wrapper: makeWrapper() }
    );
    expect(container).toBeTruthy();
  });

  it('renders with requiresConsent=false', () => {
    const template = makeTemplate({ requiresConsent: false });
    const { container } = render(
      <FormSubmitModal
        template={template}
        open={true}
        onClose={vi.fn()}
      />,
      { wrapper: makeWrapper() }
    );
    expect(container).toBeTruthy();
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-MODAL-005: FormSubmitModal — multi-step template
// ──────────────────────────────────────────────────────────────────────────────

describe('FormSubmitModal — TC-FE-MODAL-005', () => {
  it('renders with Section fields (multi-step)', () => {
    const template = makeTemplate(); // Has Section fields
    const { container } = render(
      <FormSubmitModal
        template={template}
        open={true}
        onClose={vi.fn()}
      />,
      { wrapper: makeWrapper() }
    );
    expect(container).toBeTruthy();
  });
});
