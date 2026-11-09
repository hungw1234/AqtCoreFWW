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

vi.mock('@/core/hooks/use-server-pagination', () => ({
  useServerPagination: () => ({
    antPagination: { current: 1, pageSize: 20 },
    toQueryParams: () => ({ pageNumber: 1, pageSize: 20 }),
  }),
}));

const mockUseUsers = vi.fn(() => ({ data: { items: [] as UserDto[], totalCount: 0 }, isFetching: false }));
const mockUseDeleteUser = vi.fn(() => ({ mutate: vi.fn(), isPending: false }));
const mockUseSyncRoles = vi.fn(() => ({ mutate: vi.fn(), isPending: false }));

vi.mock('@/features/users/user-api', () => ({
  useUsers: () => mockUseUsers(),
  useDeleteUser: () => mockUseDeleteUser(),
  useSyncRoles: () => mockUseSyncRoles(),
}));

// UserFormModal is a heavy modal — stub it out
vi.mock('@/features/users/user-form-modal', () => ({
  UserFormModal: () => null,
}));

import { UserListPage } from '@/features/users/user-list-page';
import type { UserDto } from '@/features/users/user-types';

function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

describe('UserListPage — basic render', () => {
  it('renders without crashing', () => {
    const { container } = render(<UserListPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });

  it('renders page title', () => {
    render(<UserListPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('page.admin.users.title')).toBeTruthy();
  });

  it('renders create user button', () => {
    render(<UserListPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('page.admin.users.create')).toBeTruthy();
  });

  it('renders user table element', () => {
    const { container } = render(<UserListPage />, { wrapper: makeWrapper() });
    expect(container.querySelector('table')).toBeTruthy();
  });

  it('renders table with user rows when data is provided', () => {
    mockUseUsers.mockReturnValue({
      data: {
        items: [
          {
            id: 'user-1',
            fullName: 'Nguyễn Văn A',
            email: 'a@gov.vn',
            roles: ['Admin'],
            clearanceLevel: 'Public',
            isActive: true,
            createdAtUtc: '2024-01-01T00:00:00Z',
          },
        ],
        totalCount: 1,
      },
      isFetching: false,
    });
    render(<UserListPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('Nguyễn Văn A')).toBeTruthy();
    expect(screen.getByText('a@gov.vn')).toBeTruthy();
  });
});
