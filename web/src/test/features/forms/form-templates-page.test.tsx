import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key }),
}));

vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => vi.fn(),
}));

vi.mock('@/features/auth/use-auth', () => ({
  useAuth: () => ({
    user: { profile: { tenant_id: 'test-tenant-id', sub: 'test-user-id', name: 'Test User' } },
    isAuthenticated: true,
  }),
}));

const mockUseFormTemplates = vi.fn(() => ({ data: { items: [] as FormTemplateDto[], totalCount: 0 }, isLoading: false }));

const mockUseCreateTemplate = vi.fn(() => ({ mutate: vi.fn(), isPending: false }));
const mockUseDeleteTemplate = vi.fn(() => ({ mutate: vi.fn(), isPending: false }));

vi.mock('@/features/forms/form-api', () => ({
  useFormTemplates: () => mockUseFormTemplates(),
  useCreateTemplate: () => mockUseCreateTemplate(),
  useDeleteTemplate: () => mockUseDeleteTemplate(),
}));

import { FormTemplatesPage } from '@/features/forms/form-templates-page';
import type { FormTemplateDto } from '@/features/forms/form-types';

function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

describe('FormTemplatesPage — render', () => {
  it('renders without crashing', () => {
    const { container } = render(<FormTemplatesPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });

  it('renders page title', () => {
    render(<FormTemplatesPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('page.forms.title')).toBeTruthy();
  });

  it('renders table element', () => {
    const { container } = render(<FormTemplatesPage />, { wrapper: makeWrapper() });
    expect(container.querySelector('table')).toBeTruthy();
  });

  it('renders column headers', () => {
    render(<FormTemplatesPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('page.forms.col.code')).toBeTruthy();
    expect(screen.getByText('page.forms.col.name')).toBeTruthy();
    expect(screen.getByText('page.forms.col.status')).toBeTruthy();
  });

  it('renders row data when templates provided', () => {
    mockUseFormTemplates.mockReturnValue({
      data: {
        items: [
          {
            id: 'tpl-1',
            code: 'FORM-001',
            name: 'Đơn xin cấp phép',
            status: 'Active' as const,
            version: 1,
            fields: [],
            submissionsCount: 5,
            createdAt: '2024-01-01T00:00:00Z',
          },
        ],
        totalCount: 1,
      },
      isLoading: false,
    });
    render(<FormTemplatesPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('FORM-001')).toBeTruthy();
    expect(screen.getByText('Đơn xin cấp phép')).toBeTruthy();
  });
});
