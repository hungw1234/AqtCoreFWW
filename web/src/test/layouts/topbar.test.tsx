import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

// TC-FE-LAY-004: Topbar shows user name + logout

// Use importOriginal to preserve initReactI18next (needed by i18n-config.ts side-effects)
vi.mock('react-i18next', async (importOriginal) => {
  const actual = await importOriginal<typeof import('react-i18next')>();
  return {
    ...actual,
    useTranslation: () => ({ t: (key: string) => key }),
  };
});

vi.mock('@tanstack/react-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@tanstack/react-router')>();
  return {
    ...actual,
    useLocation: () => ({ pathname: '/' }),
    useNavigate: () => vi.fn(),
  };
});

vi.mock('@/features/auth/use-auth', () => ({
  useAuth: () => ({
    user: { profile: { name: 'Nguyen Van A', sub: 'user-1' } },
    isAuthenticated: true,
    logout: vi.fn(),
  }),
}));

vi.mock('@/core/theme/theme-switcher', () => ({
  ThemeSwitcher: () => <div data-testid="theme-switcher-mock" />,
}));

// Topbar renders NotificationBell which uses useQueryClient + API hooks — stub them here
vi.mock('@/features/notifications/notification-signalr', () => ({
  startSignalR: vi.fn(),
  onNotification: vi.fn(() => () => {}),
}));

vi.mock('@/features/notifications/notification-api', () => ({
  useUnreadCount: () => ({ data: 0 }),
  useNotifications: () => ({ data: { items: [] } }),
  useMarkAsRead: () => ({ mutate: vi.fn() }),
  useMarkAllAsRead: () => ({ mutate: vi.fn(), isPending: false }),
}));

import { Topbar } from '@/layouts/topbar';

function makeQueryWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

describe('Topbar — TC-FE-LAY-004', () => {
  it('renders without crashing', () => {
    const { container } = render(<Topbar />, { wrapper: makeQueryWrapper() });
    expect(container.firstChild).toBeTruthy();
  });

  it('displays the user display name', () => {
    render(<Topbar />, { wrapper: makeQueryWrapper() });
    expect(screen.getByText('Nguyen Van A')).toBeTruthy();
  });

  it('renders user menu button with aria-label containing user name', () => {
    const { container } = render(<Topbar />, { wrapper: makeQueryWrapper() });
    const btn = container.querySelector('button[aria-haspopup="true"]');
    expect(btn).toBeTruthy();
    expect(btn?.getAttribute('aria-label')).toContain('Nguyen Van A');
  });

  it('renders notification bell icon', () => {
    const { container } = render(<Topbar />, { wrapper: makeQueryWrapper() });
    // BellOutlined renders as an ant-design icon span
    expect(container.querySelector('[aria-label="bell"]')).toBeTruthy();
  });
});
