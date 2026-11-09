import { describe, it, expect, vi } from 'vitest';
import { render } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { NotificationDto } from '@/features/notifications/notification-types';

// Mock TanStack Router navigate
vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => vi.fn(),
}));

// Mock SignalR — startSignalR/onNotification are side-effects not needed here
vi.mock('@/features/notifications/notification-signalr', () => ({
  startSignalR: vi.fn(),
  onNotification: vi.fn(() => () => {}), // returns unsubscribe fn
}));

// Mock notification API hooks with controllable return values
const mockUseUnreadCount = vi.fn(() => ({ data: 0 as number | undefined }));
const mockUseNotifications = vi.fn(() => ({ data: { items: [] as NotificationDto[] } }));
const mockUseMarkAsRead = vi.fn(() => ({ mutate: vi.fn() }));
const mockUseMarkAllAsRead = vi.fn(() => ({ mutate: vi.fn(), isPending: false }));

vi.mock('@/features/notifications/notification-api', () => ({
  useUnreadCount: () => mockUseUnreadCount(),
  useNotifications: () => mockUseNotifications(),
  useMarkAsRead: () => mockUseMarkAsRead(),
  useMarkAllAsRead: () => mockUseMarkAllAsRead(),
}));

// dayjs is a real dep — no need to mock

import { NotificationBell } from '@/features/notifications/notification-bell';

function makeQueryWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

describe('NotificationBell — unread count badge', () => {
  it('renders without crashing with zero unread count', () => {
    mockUseUnreadCount.mockReturnValue({ data: 0 });
    const { container } = render(<NotificationBell />, { wrapper: makeQueryWrapper() });
    expect(container.firstChild).toBeTruthy();
  });

  it('renders bell icon (BellOutlined svg)', () => {
    mockUseUnreadCount.mockReturnValue({ data: 5 });
    const { container } = render(<NotificationBell />, { wrapper: makeQueryWrapper() });
    // Ant Design icons render as SVG elements
    const svg = container.querySelector('svg');
    expect(svg).toBeTruthy();
  });

  it('shows unread count of 3 in Badge via title attribute', () => {
    mockUseUnreadCount.mockReturnValue({ data: 3 });
    const { container } = render(<NotificationBell />, { wrapper: makeQueryWrapper() });
    // Ant Design Badge sets title="N" on the <sup> element
    const badge = container.querySelector('sup[title="3"]');
    expect(badge).toBeTruthy();
  });

  it('shows unread count of 99 in Badge via title attribute', () => {
    mockUseUnreadCount.mockReturnValue({ data: 99 });
    const { container } = render(<NotificationBell />, { wrapper: makeQueryWrapper() });
    // Ant Design Badge renders digits in separate spans; use title attribute to verify count
    const badge = container.querySelector('sup[title="99"]');
    expect(badge).toBeTruthy();
  });

  it('shows no badge sup when count is 0', () => {
    mockUseUnreadCount.mockReturnValue({ data: 0 });
    const { container } = render(<NotificationBell />, { wrapper: makeQueryWrapper() });
    // Badge with count=0 hides the sup element (data-show="false" or absent)
    const badge = container.querySelector('sup[data-show="true"]');
    expect(badge).toBeNull();
  });

  it('handles undefined unreadData gracefully (defaults to 0)', () => {
    mockUseUnreadCount.mockReturnValue({ data: undefined });
    const { container } = render(<NotificationBell />, { wrapper: makeQueryWrapper() });
    expect(container.firstChild).toBeTruthy();
  });
});

describe('NotificationBell — notification list', () => {
  it('renders empty notification list without crashing', () => {
    mockUseUnreadCount.mockReturnValue({ data: 0 });
    mockUseNotifications.mockReturnValue({ data: { items: [] } });
    const { container } = render(<NotificationBell />, { wrapper: makeQueryWrapper() });
    expect(container.firstChild).toBeTruthy();
  });
});
