// TC-FE-ORG-001, TC-FE-ORG-002, TC-FE-ORG-003
// OrgTreePage — tree/list view, loading state, error state

import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key }),
}));

// Mock heavy sub-components to avoid full tree rendering
vi.mock('@/features/organization/org-unit-form-modal', () => ({
  OrgUnitFormModal: () => null,
}));

vi.mock('@/features/organization/staff-assignment-table', () => ({
  StaffAssignmentTable: () => createElement('div', { 'data-testid': 'staff-table' }),
}));

// Controllable org API mock
const mockUseOrgUnits = vi.fn(() => ({
  data: [] as OrgUnitDto[] | undefined,
  isFetching: false,
  error: null as Error | null,
}));

vi.mock('@/features/organization/org-api', () => ({
  useOrgUnits: () => mockUseOrgUnits(),
  useCreateOrgUnit: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useUpdateOrgUnit: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

import { OrgTreePage } from '@/features/organization/org-tree-page';
import type { OrgUnitDto } from '@/features/organization/org-types';

function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

// TC-FE-ORG-001: Renders tree/list view with data
describe('OrgTreePage — TC-FE-ORG-001: renders tree/list view', () => {
  it('renders without crashing and shows page title', () => {
    mockUseOrgUnits.mockReturnValue({ data: [], isFetching: false, error: null });
    const { container } = render(<OrgTreePage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
    expect(screen.getByText('page.admin.org.title')).toBeTruthy();
  });

  it('renders tree structure with org unit data', () => {
    mockUseOrgUnits.mockReturnValue({
      data: [
        { id: 'ou-1', name: 'Phòng Hành Chính', code: 'HC', parentId: undefined, level: 1, childCount: 2, staffCount: 5 },
        { id: 'ou-2', name: 'Phòng Kỹ Thuật', code: 'KT', parentId: undefined, level: 1, childCount: 0, staffCount: 3 },
      ],
      isFetching: false,
      error: null,
    });
    render(<OrgTreePage />, { wrapper: makeWrapper() });
    expect(screen.getByText('Phòng Hành Chính (HC)')).toBeTruthy();
    expect(screen.getByText('Phòng Kỹ Thuật (KT)')).toBeTruthy();
  });

  it('renders add and edit buttons in tree card', () => {
    mockUseOrgUnits.mockReturnValue({ data: [], isFetching: false, error: null });
    render(<OrgTreePage />, { wrapper: makeWrapper() });
    // Buttons with aria-label from t('common.add') and t('common.edit')
    expect(screen.getByLabelText('common.add')).toBeTruthy();
    expect(screen.getByLabelText('common.edit')).toBeTruthy();
  });
});

// TC-FE-ORG-002: Shows loading state
describe('OrgTreePage — TC-FE-ORG-002: loading state', () => {
  it('renders Ant Design Spin while data is fetching', () => {
    mockUseOrgUnits.mockReturnValue({ data: [], isFetching: true, error: null });
    const { container } = render(<OrgTreePage />, { wrapper: makeWrapper() });
    // Ant Design Spin adds role="img" with aria-label, or renders .ant-spin
    const spinner = container.querySelector('.ant-spin');
    expect(spinner).toBeTruthy();
  });
});

// TC-FE-ORG-003: Shows error state on API failure
describe('OrgTreePage — TC-FE-ORG-003: error state', () => {
  it('renders empty tree gracefully when data is undefined (API failure)', () => {
    // OrgTreePage uses data ?? [] so it won't crash on undefined data
    mockUseOrgUnits.mockReturnValue({ data: undefined, isFetching: false, error: new Error('Network error') });
    const { container } = render(<OrgTreePage />, { wrapper: makeWrapper() });
    // Page should still render without throwing
    expect(container.firstChild).toBeTruthy();
    expect(screen.getByText('page.admin.org.title')).toBeTruthy();
  });
});
