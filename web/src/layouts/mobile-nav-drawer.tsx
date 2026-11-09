import { Drawer } from 'antd';
import { GOV_COLORS } from '@/app/theme';
import { SidebarMenu } from './sidebar-menu';

import type { MenuNodeDto } from '@/features/menus';

interface MobileNavDrawerProps {
  open: boolean;
  onClose: () => void;
  permissions: string[];
  roles: string[];
  menuTree?: MenuNodeDto[];
}

// Brand logo area — mirrors the Sider logo in app-layout
function DrawerLogo() {
  return (
    <div
      style={{
        height: 56,
        display: 'flex',
        alignItems: 'center',
        padding: '0 20px',
        color: GOV_COLORS.white,
        fontWeight: 700,
        fontSize: 16,
        fontFamily: 'var(--font-heading)',
        letterSpacing: 0.5,
      }}
    >
      <span
        style={{
          display: 'inline-block',
          width: 24,
          height: 24,
          borderRadius: 4,
          background: GOV_COLORS.gold,
          marginRight: 10,
          flexShrink: 0,
        }}
      />
      AqtCoreFW
    </div>
  );
}

// MobileNavDrawer: full-height overlay navigation for mobile (<768px)
// Reuses SidebarMenu with an onClose override so navigation closes the drawer.
export function MobileNavDrawer({ open, onClose, permissions, roles, menuTree }: MobileNavDrawerProps) {
  return (
    <Drawer
      open={open}
      onClose={onClose}
      placement="left"
      width={280}
      closable
      className="mobile-nav-drawer"
      title={<DrawerLogo />}
      styles={{
        header: {
          background: GOV_COLORS.navyDeep,
          borderBottom: '1px solid rgba(255, 255, 255, 0.1)',
          padding: 0,
        },
        body: {
          padding: 0,
          background: 'var(--sidebar-bg)',
        },
      }}
    >
      {/* Divider below logo, matching Sider style */}
      <div style={{ height: 1, background: 'rgba(255,255,255,0.1)', margin: '0 16px 8px' }} />
      {/* SidebarMenu with onClick override to close drawer on navigation */}
      <SidebarMenu
        permissions={permissions}
        roles={roles}
        menuTree={menuTree}
        onNavigate={onClose}
      />
    </Drawer>
  );
}
