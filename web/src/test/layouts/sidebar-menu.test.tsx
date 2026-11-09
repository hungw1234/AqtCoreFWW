import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';

// TC-FE-LAY-002: SidebarMenu renders admin submenus for admin role (Option C flat structure)
// TC-FE-LAY-003: SidebarMenu hides admin submenus for regular user

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key }),
}));

vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => vi.fn(),
  useLocation: () => ({ pathname: '/' }),
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

describe('SidebarMenu — TC-FE-LAY-002: admin role sees admin submenus', () => {
  it('renders admin submenus (flat Option C structure) for SystemAdmin role', () => {
    render(<SidebarMenu permissions={[]} roles={['SystemAdmin']} />);
    // Option C — 6 flat admin submenu labels + overview should all be visible
    expect(screen.getByText('nav.adminIdentity')).toBeTruthy();
    expect(screen.getByText('nav.adminContent')).toBeTruthy();
    expect(screen.getByText('nav.adminSystem')).toBeTruthy();
  });

  it('renders standard nav items for all users', () => {
    render(<SidebarMenu permissions={[]} roles={[]} />);
    expect(screen.getByText('nav.dashboard')).toBeTruthy();
    expect(screen.getByText('nav.inbox')).toBeTruthy();
    expect(screen.getByText('nav.forms')).toBeTruthy();
  });

  it('renders audit item when roles include GovOfficer', () => {
    render(<SidebarMenu permissions={[]} roles={['GovOfficer']} />);
    expect(screen.getByText('nav.audit')).toBeTruthy();
  });
});

describe('SidebarMenu — TC-FE-LAY-003: regular user has no admin submenus', () => {
  it('does not render any admin submenu for user with no roles/permissions', () => {
    render(<SidebarMenu permissions={[]} roles={[]} />);
    // Option C — all admin-* submenus hidden without IDENTITY.USER.VIEW
    expect(screen.queryByText('nav.adminIdentity')).toBeNull();
    expect(screen.queryByText('nav.adminSystem')).toBeNull();
  });

  it('does not render audit item for regular user', () => {
    render(<SidebarMenu permissions={[]} roles={[]} />);
    expect(screen.queryByText('nav.audit')).toBeNull();
  });

  it('does not render roles item for regular user', () => {
    render(<SidebarMenu permissions={[]} roles={[]} />);
    // nav.roles lives inside admin-identity submenu; when parent hidden, children not rendered
    expect(screen.queryByText('nav.roles')).toBeNull();
  });
});
