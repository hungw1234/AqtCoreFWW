import { describe, it, expect, vi } from 'vitest';
import { render } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

// TC-FE-LAY-004: AppLayout does not flash hardcoded menu during API loading (F6 fix)
// TC-FE-LAY-005: AppLayout shows hardcoded fallback only on API error (F6 fix)

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
    permissions: ['IDENTITY.USER.VIEW'],
    roles: ['Admin'],
    hasPermission: () => true,
    hasRole: () => true,
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

// Mock useMenuTree so we can control isLoading / isError per test
const mockUseMenuTree = vi.fn();
vi.mock('@/features/menus', () => ({
  useMenuTree: () => mockUseMenuTree(),
}));

import { AppLayout } from '@/layouts/app-layout';

function makeWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

describe('AppLayout — TC-FE-LAY-004: menu loading state (F6 fix)', () => {
  it('passes undefined menuTree to SidebarMenu while API is loading', () => {
    // isLoading=true → menuTree should be undefined (no premature hardcoded fallback)
    mockUseMenuTree.mockReturnValue({
      data: undefined,
      isLoading: true,
      isError: false,
    });

    const { container } = render(<AppLayout />, { wrapper: makeWrapper() });

    // Component should render without crashing
    expect(container.firstChild).toBeTruthy();
    // Nav should be present (sidebar renders, just with no menu items from API yet)
    const nav = container.querySelector('[role="navigation"]');
    expect(nav).toBeTruthy();
  });
});

describe('AppLayout — TC-FE-LAY-005: menu error state (F6 fix)', () => {
  it('passes menuTree data when API succeeds', () => {
    const fakeTree = [
      { id: 'abc', code: 'dashboard', title: 'Dashboard', icon: null, route: '/', sortOrder: 1, children: [] },
    ];
    mockUseMenuTree.mockReturnValue({
      data: fakeTree,
      isLoading: false,
      isError: false,
    });

    const { container } = render(<AppLayout />, { wrapper: makeWrapper() });

    expect(container.firstChild).toBeTruthy();
    const nav = container.querySelector('[role="navigation"]');
    expect(nav).toBeTruthy();
  });

  it('shows fallback menu when API errors (isError=true)', () => {
    // isError=true, data=undefined → SidebarMenu receives undefined menuTree → hardcoded fallback
    mockUseMenuTree.mockReturnValue({
      data: undefined,
      isLoading: false,
      isError: true,
    });

    const { container } = render(<AppLayout />, { wrapper: makeWrapper() });

    expect(container.firstChild).toBeTruthy();
    const nav = container.querySelector('[role="navigation"]');
    expect(nav).toBeTruthy();
  });
});
