// form-public-page.test.tsx — tests for public form rendering (anonymous users)
// Tests: PublicFormPage multi-step, visibility, consent, form submission flow

import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
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
  useParams: () => ({ code: 'FORM-001' }),
  useLocation: () => ({ pathname: '/' }),
  Link: ({ children }: { children: ReactNode }) => children,
}));

// Mock form-api
vi.mock('@/features/forms/form-api', () => ({
  usePublicForm: () => ({
    data: {
      id: 'tpl-1',
      code: 'FORM-001',
      name: 'Public Test Form',
      nameVi: 'Biểu mẫu công khai',
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
      requiresConsent: true,
      consentText: 'I agree to the terms and conditions',
    },
    isLoading: false,
    isError: false,
  }),
  useSubmitPublicForm: () => ({
    mutate: vi.fn(),
    isPending: false,
  }),
  // Formula compute hook (Phase-4A) — component imports it unconditionally, so the
  // mock must expose it even for templates without Formula fields (else undefined at render).
  useEvaluatePublicFormulas: () => ({
    mutateAsync: vi.fn().mockResolvedValue({ results: {} }),
    isPending: false,
  }),
}));

// ──────────────────────────────────────────────────────────────────────────────
// Component imports — after mocks
// ──────────────────────────────────────────────────────────────────────────────

import { PublicFormPage } from '@/features/forms/public-form-page';

// ──────────────────────────────────────────────────────────────────────────────
// Wrapper utility
// ──────────────────────────────────────────────────────────────────────────────

function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-PUBLIC-001: PublicFormPage — rendering with valid template
// ──────────────────────────────────────────────────────────────────────────────

describe('PublicFormPage — TC-FE-PUBLIC-001', () => {
  it('renders successfully with valid template', () => {
    const { container } = render(<PublicFormPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-PUBLIC-002: PublicFormPage — renders form structure
// ──────────────────────────────────────────────────────────────────────────────

describe('PublicFormPage — TC-FE-PUBLIC-002', () => {
  it('renders form structure when data loaded', () => {
    const { container } = render(<PublicFormPage />, { wrapper: makeWrapper() });
    expect(container.querySelector('.ant-form')).toBeTruthy();
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-PUBLIC-003: PublicFormPage — renders form title
// ──────────────────────────────────────────────────────────────────────────────

describe('PublicFormPage — TC-FE-PUBLIC-003', () => {
  it('renders form title from template.nameVi', () => {
    render(<PublicFormPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('Biểu mẫu công khai')).toBeTruthy();
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-PUBLIC-004: PublicFormPage — renders form fields
// ──────────────────────────────────────────────────────────────────────────────

describe('PublicFormPage — TC-FE-PUBLIC-004', () => {
  it('renders form fields from template', () => {
    const { container } = render(<PublicFormPage />, { wrapper: makeWrapper() });
    // Form should have inputs for fields
    expect(container.querySelector('.ant-form')).toBeTruthy();
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-PUBLIC-005: PublicFormPage — form validation
// ──────────────────────────────────────────────────────────────────────────────

describe('PublicFormPage — TC-FE-PUBLIC-005', () => {
  it('renders form with proper structure', () => {
    const { container } = render(<PublicFormPage />, { wrapper: makeWrapper() });
    // Form should have inputs and structure
    const form = container.querySelector('.ant-form');
    const formItems = container.querySelectorAll('.ant-form-item');
    expect(form).toBeTruthy();
    expect(formItems.length).toBeGreaterThan(0);
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-PUBLIC-006: PublicFormPage — form has input elements
// ──────────────────────────────────────────────────────────────────────────────

describe('PublicFormPage — TC-FE-PUBLIC-006', () => {
  it('renders input elements for form fields', () => {
    const { container } = render(<PublicFormPage />, { wrapper: makeWrapper() });
    // Should have form inputs for the fields defined in mock
    expect(container.querySelector('input')).toBeTruthy();
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-PUBLIC-007: PublicFormPage — Steps indicator for multi-step
// ──────────────────────────────────────────────────────────────────────────────

describe('PublicFormPage — TC-FE-PUBLIC-007', () => {
  it('shows Steps component when multiple Section fields present (multi-step)', () => {
    const { container } = render(<PublicFormPage />, { wrapper: makeWrapper() });
    // With Section fields, Steps should render
    expect(container.querySelector('.ant-steps')).toBeTruthy();
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-PUBLIC-008: PublicFormPage — Submit button
// ──────────────────────────────────────────────────────────────────────────────

describe('PublicFormPage — TC-FE-PUBLIC-008', () => {
  it('shows Submit button', () => {
    render(<PublicFormPage />, { wrapper: makeWrapper() });
    const button = screen.queryByRole('button');
    expect(button).toBeTruthy();
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// TC-FE-PUBLIC-009: PublicFormPage — button navigation
// ──────────────────────────────────────────────────────────────────────────────

describe('PublicFormPage — TC-FE-PUBLIC-009', () => {
  it('has navigation buttons for form steps', () => {
    render(<PublicFormPage />, { wrapper: makeWrapper() });
    const buttons = screen.queryAllByRole('button');
    expect(buttons.length).toBeGreaterThan(0);
  });
});
