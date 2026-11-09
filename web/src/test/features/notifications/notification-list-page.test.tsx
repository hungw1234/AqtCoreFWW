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

const mockUseNotifications = vi.fn(() => ({ data: { items: [] as NotificationDto[], totalCount: 0 }, isLoading: false }));
const mockUseMarkAsRead = vi.fn(() => ({ mutate: vi.fn(), isPending: false }));
const mockUseMarkAllAsRead = vi.fn(() => ({ mutate: vi.fn(), isPending: false }));

vi.mock('@/features/notifications/notification-api', () => ({
  useNotifications: () => mockUseNotifications(),
  useMarkAsRead: () => mockUseMarkAsRead(),
  useMarkAllAsRead: () => mockUseMarkAllAsRead(),
}));

import { NotificationListPage } from '@/features/notifications/notification-list-page';
import type { NotificationDto } from '@/features/notifications/notification-types';

function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

describe('NotificationListPage — render', () => {
  it('renders without crashing', () => {
    const { container } = render(<NotificationListPage />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });

  it('renders page title', () => {
    render(<NotificationListPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('page.notifications.title')).toBeTruthy();
  });

  it('renders mark-all-read button', () => {
    render(<NotificationListPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('page.notifications.markAllRead')).toBeTruthy();
  });

  it('renders notification table', () => {
    const { container } = render(<NotificationListPage />, { wrapper: makeWrapper() });
    expect(container.querySelector('table')).toBeTruthy();
  });
});

describe('NotificationListPage — with data', () => {
  it('renders unread notification with NEW tag', () => {
    mockUseNotifications.mockReturnValue({
      data: {
        items: [
          {
            id: 'n1',
            subject: 'Hồ sơ đã được duyệt',
            body: 'Hồ sơ HS-001 đã được duyệt',
            isRead: false,
            createdAt: '2024-01-20T10:00:00Z',
            deepLink: '/cases/case-1',
          },
        ],
        totalCount: 1,
      },
      isLoading: false,
    });
    render(<NotificationListPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('Hồ sơ đã được duyệt')).toBeTruthy();
    // Unread tag
    expect(screen.getByText('page.notifications.newTag')).toBeTruthy();
  });

  it('renders mark-read button for unread notification', () => {
    render(<NotificationListPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('page.notifications.markRead')).toBeTruthy();
  });

  it('renders read notification without NEW tag', () => {
    mockUseNotifications.mockReturnValue({
      data: {
        items: [
          {
            id: 'n2',
            subject: 'Thông báo đã đọc',
            body: 'Nội dung',
            isRead: true,
            createdAt: '2024-01-19T08:00:00Z',
          },
        ],
        totalCount: 1,
      },
      isLoading: false,
    });
    render(<NotificationListPage />, { wrapper: makeWrapper() });
    expect(screen.getByText('Thông báo đã đọc')).toBeTruthy();
    expect(screen.queryByText('page.notifications.newTag')).toBeNull();
  });
});
