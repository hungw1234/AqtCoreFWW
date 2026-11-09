import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key }),
}));

// useUser drives the edit-detail fetch whose state gates the save button. A vi.fn so each test
// can set loading / error / success without re-mocking the module.
const mockUseUser = vi.fn();
const noopMutation = () => ({ mutateAsync: vi.fn(), isPending: false });

vi.mock('@/features/users/user-api', () => ({
  useCreateUser: () => noopMutation(),
  useUpdateUser: () => noopMutation(),
  useAssignStaff: () => noopMutation(),
  useUser: () => mockUseUser(),
}));

vi.mock('@/features/organization/org-api', () => ({
  useOrgUnits: () => ({ data: [] }),
}));

vi.mock('@/core/api/notification-bridge', () => ({
  appMessage: { success: vi.fn(), error: vi.fn() },
}));

import { UserFormModal } from '@/features/users/user-form-modal';
import type { UserDto } from '@/features/users/user-types';

const EDIT_USER: UserDto = {
  id: 'user-1',
  fullName: 'Nguyễn Văn A',
  email: 'a@gov.vn',
  departmentCode: 'IT',
  roles: ['Admin'],
  clearanceLevel: 'Public',
  isActive: true,
  tenantId: 'tenant-1',
  createdAtUtc: '2024-01-01T00:00:00Z',
} as UserDto;

function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

function saveButton(): HTMLButtonElement {
  return screen.getByRole('button', { name: 'common.save' }) as HTMLButtonElement;
}

describe('UserFormModal — CCCD save gate (edit detail must load before save)', () => {
  beforeEach(() => mockUseUser.mockReset());

  it('blocks save while the edit detail is still loading', () => {
    // CCCD field is empty because detail has not arrived — saving the full-replace PUT now would
    // send an empty signingNationalId and wipe the stored value.
    mockUseUser.mockReturnValue({ data: undefined, isLoading: true, isError: false });
    render(<UserFormModal open editUser={EDIT_USER} onClose={() => {}} />, { wrapper: makeWrapper() });

    expect(saveButton().disabled).toBe(true);
  });

  it('blocks save when the edit detail fetch errored (regression: CCCD-PUT-ABSENT)', () => {
    // The empty CCCD field is empty for the WRONG reason (load failed), not because the user cleared
    // it — so the save must stay disabled even when editing an unrelated field.
    mockUseUser.mockReturnValue({ data: undefined, isLoading: false, isError: true });
    render(<UserFormModal open editUser={EDIT_USER} onClose={() => {}} />, { wrapper: makeWrapper() });

    expect(saveButton().disabled).toBe(true);
    expect(screen.getByText('page.admin.users.detailLoadError')).toBeTruthy();
  });

  it('allows save once the edit detail loaded successfully', () => {
    mockUseUser.mockReturnValue({
      data: { ...EDIT_USER, signingNationalId: '079201001234' },
      isLoading: false,
      isError: false,
    });
    render(<UserFormModal open editUser={EDIT_USER} onClose={() => {}} />, { wrapper: makeWrapper() });

    expect(saveButton().disabled).toBe(false);
  });
});
