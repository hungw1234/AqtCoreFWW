import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';

// The Hangfire dashboard nav item is a SystemAdmin-only EXTERNAL link (opens the AuthServer-hosted
// dashboard in a new tab), not a router route. These pin: role-gated visibility + the link-out behavior.

vi.mock('react-i18next', () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
// pathname under SYSTEM_PATHS → the admin-system submenu is in defaultOpenKeys → its children
// (incl the Hangfire item) render into the DOM, so visibility/gating is actually exercised.
vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => vi.fn(),
  useLocation: () => ({ pathname: '/admin/jobs' }),
}));
// SidebarMenu now reads deferred-module flags via react-query; mock the hook (everything
// enabled = default posture) so these menu tests need no QueryClientProvider. Spread the
// real module so sibling exports stay intact (enumerated-exports trap).
vi.mock('@/features/auth/module-flags-api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/features/auth/module-flags-api')>();
  return {
    ...actual,
    useModuleFlags: () => ({ flags: actual.ALL_MODULES_ENABLED, isFlagsLoading: false }),
  };
});

import { SidebarMenu } from '@/layouts/sidebar-menu';

describe('SidebarMenu — Hangfire external link', () => {
  afterEach(() => vi.restoreAllMocks());

  it('shows the Hangfire item for SystemAdmin', () => {
    render(<SidebarMenu permissions={[]} roles={['SystemAdmin']} />);
    expect(screen.getByText('nav.hangfire')).toBeTruthy();
  });

  it('hides the Hangfire item for a non-SystemAdmin admin (Admin role)', () => {
    // admin-system submenu IS visible to Admin, but the Hangfire child is SystemAdmin-gated → absent.
    render(<SidebarMenu permissions={[]} roles={['Admin']} />);
    expect(screen.queryByText('nav.hangfire')).toBeNull();
  });

  it('hides the Hangfire item for a regular user', () => {
    render(<SidebarMenu permissions={[]} roles={[]} />);
    expect(screen.queryByText('nav.hangfire')).toBeNull();
  });

  // The active path in a seeded dev/prod environment is the API-driven menu tree (not the hardcoded
  // fallback). The Hangfire external link must render there too (appended for SystemAdmin), else it's
  // dead in dev despite the fallback-path tests passing.
  const apiMenuTree = [
    { id: '1', code: 'home', title: 'Home', description: null, icon: 'DashboardOutlined', route: '/', sortOrder: 0, children: [] },
  ];

  it('shows the Hangfire item on the API-driven menu path for SystemAdmin', () => {
    render(<SidebarMenu permissions={[]} roles={['SystemAdmin']} menuTree={apiMenuTree} />);
    expect(screen.getByText('nav.hangfire')).toBeTruthy();
  });

  it('hides the Hangfire item on the API-driven menu path for a non-SystemAdmin', () => {
    render(<SidebarMenu permissions={[]} roles={['Admin']} menuTree={apiMenuTree} />);
    expect(screen.queryByText('nav.hangfire')).toBeNull();
  });

  it('opens the AuthServer dashboard in a new tab on click (no router navigation)', () => {
    const openSpy = vi.spyOn(window, 'open').mockImplementation(() => null);
    render(<SidebarMenu permissions={[]} roles={['SystemAdmin']} />);

    fireEvent.click(screen.getByText('nav.hangfire'));

    // VITE_OIDC_AUTHORITY is unset in tests → auth-config falls back to http://localhost:5002.
    expect(openSpy).toHaveBeenCalledWith('http://localhost:5002/hangfire', '_blank', 'noopener,noreferrer');
  });
});
