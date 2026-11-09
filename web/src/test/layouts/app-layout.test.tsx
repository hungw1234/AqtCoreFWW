import { describe, it, expect, vi } from 'vitest';
import { render } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

// TC-FE-LAY-001: AppLayout renders sidebar + content area

// Use importOriginal so react-i18next keeps initReactI18next (needed by i18n-config.ts)
vi.mock('react-i18next', async (importOriginal) => {
  const actual = await importOriginal<typeof import('react-i18next')>();
  return {
    ...actual,
    useTranslation: () => ({ t: (key: string) => key, i18n: { language: 'vi' } }),
  };
});

vi.mock('@tanstack/react-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@tanstack/react-router')>();
  return {
    ...actual,
    useNavigate: () => vi.fn(),
    useLocation: () => ({ pathname: '/' }),
    Outlet: () => createElement('div', { 'data-testid': 'outlet' }),
  };
});

vi.mock('@/features/auth/use-auth', () => ({
  useAuth: () => ({
    user: { profile: { sub: 'test-user', name: 'Test User' } },
    isAuthenticated: true,
    logout: vi.fn(),
  }),
}));

vi.mock('@/features/auth/use-permissions', () => ({
  usePermissions: () => ({
    permissions: [],
    roles: [],
    hasPermission: () => false,
    hasRole: () => false,
  }),
}));

vi.mock('@/features/notifications/notification-bell', () => ({
  NotificationBell: () => createElement('div', { 'data-testid': 'notification-bell' }),
}));

vi.mock('@/features/notifications/notification-signalr', () => ({
  startSignalR: vi.fn(),
  onNotification: vi.fn(() => () => {}),
}));

vi.mock('@/core/i18n/language-switcher', () => ({
  LanguageSwitcher: () => createElement('div', { 'data-testid': 'lang-switcher' }),
}));

vi.mock('@/core/theme/theme-switcher', () => ({
  ThemeSwitcher: () => createElement('div', { 'data-testid': 'theme-switcher' }),
}));

import { AppLayout } from '@/layouts/app-layout';

function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

describe('AppLayout — TC-FE-LAY-001', () => {
  it('renders sidebar nav + main content area without crashing', () => {
    const { container } = render(<AppLayout />, { wrapper: makeWrapper() });
    expect(container.firstChild).toBeTruthy();
  });

  it('renders a navigation landmark (sidebar menu)', () => {
    const { container } = render(<AppLayout />, { wrapper: makeWrapper() });
    // SidebarMenu renders <nav role="navigation">
    const nav = container.querySelector('[role="navigation"]');
    expect(nav).toBeTruthy();
  });

  it('renders the main content region', () => {
    const { container } = render(<AppLayout />, { wrapper: makeWrapper() });
    const main = container.querySelector('[role="main"]');
    expect(main).toBeTruthy();
  });

  it('renders skip-to-content accessibility link', () => {
    const { container } = render(<AppLayout />, { wrapper: makeWrapper() });
    const skipLink = container.querySelector('a[href="#main-content"]');
    expect(skipLink).toBeTruthy();
  });
});
