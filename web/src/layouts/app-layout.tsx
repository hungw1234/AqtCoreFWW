import { Suspense, useEffect, useState } from 'react';
import { Button } from 'antd';
import { MenuOutlined } from '@ant-design/icons';
import { Outlet } from '@tanstack/react-router';
import { RouteLoadingSpinner } from '@/shared/components/route-loading-spinner';
import { GOV_COLORS, LAYOUT } from '@/app/theme';
import { useThemeMode } from '@/core/theme/use-theme-mode';
import { usePermissions } from '@/features/auth';
import { useModuleFlags } from '@/features/auth/module-flags-api';
import { useMenuTree } from '@/features/menus';
import { CopilotProvider, CopilotSidebar, CopilotToggleButton } from '@/features/copilot';
import { useLayoutMode } from '@/core/hooks/use-layout-mode';
import { SidebarMenu } from './sidebar-menu';
import { MobileNavDrawer } from './mobile-nav-drawer';
import { SettingsDrawer } from './settings-drawer';
import { Topbar } from './topbar';

// AppLayout: fixed header + fixed sidebar + scrollable content (no jank on scroll)
//   Desktop (≥1024px): Fixed header full-width + Fixed Sider 220px + scrollable content
//   Tablet  (768-1023): Fixed header + Fixed Sider collapsed 72px + scrollable content
//   Mobile  (<768px):   Fixed header + drawer overlay (no Sider)
export function AppLayout() {
  const [collapsed, setCollapsed] = useState(false);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [popupSubMenus, setPopupSubMenus] = useState(false);

  const layoutMode = useLayoutMode();
  const { flags } = useModuleFlags();

  // Auto-collapse sidebar when entering tablet mode, expand on desktop
  useEffect(() => {
    setCollapsed(layoutMode === 'tablet');
  }, [layoutMode]);

  const { isDark } = useThemeMode();
  const { permissions, roles } = usePermissions();
  const { data: menuTree, isLoading: menuLoading } = useMenuTree();

  const contentPadding =
    layoutMode === 'mobile'
      ? '16px'
      : layoutMode === 'tablet'
      ? `${LAYOUT.contentPadding}px 24px`
      : `${LAYOUT.contentPadding}px ${LAYOUT.contentPaddingH}px`;

  const siderCollapsed = collapsed;
  const siderWidth = siderCollapsed ? LAYOUT.siderCollapsedWidth : LAYOUT.siderWidth;

  return (
    <CopilotProvider>
    <div style={{ minHeight: '100vh' }}>
      {/* Skip navigation — WCAG 2.4.1 */}
      <a
        href="#main-content"
        style={{ position: 'absolute', left: '-9999px', top: 'auto', width: '1px', height: '1px', overflow: 'hidden' }}
        onFocus={(e) => { e.currentTarget.style.cssText = 'position:fixed;top:8px;left:8px;z-index:9999;padding:8px 16px;background:var(--gov-navy);color:var(--gov-bg-card);border-radius:4px;text-decoration:none;font-weight:600;'; }}
        onBlur={(e) => { e.currentTarget.style.cssText = 'position:absolute;left:-9999px;top:auto;width:1px;height:1px;overflow:hidden;'; }}
      >
        Skip to main content
      </a>

      {/* FIXED header — never scrolls */}
      <header
        role="banner"
        style={{
          position: 'fixed',
          top: 0,
          left: 0,
          right: 0,
          height: LAYOUT.headerHeight,
          display: 'flex',
          alignItems: 'center',
          borderBottom: '1px solid var(--gov-border-color)',
          background: isDark ? 'var(--gov-bg-card)' : GOV_COLORS.white,
          boxShadow: '0 1px 2px rgba(0,0,0,0.04)',
          zIndex: 100,
        }}
      >
        {/* Mobile + Tablet hamburger — mobile opens drawer, tablet toggles sidebar collapse */}
        {(layoutMode === 'mobile' || layoutMode === 'tablet') && (
          <Button
            type="text"
            icon={<MenuOutlined />}
            onClick={() => layoutMode === 'mobile' ? setDrawerOpen(true) : setCollapsed((c) => !c)}
            aria-label="Open navigation menu"
            style={{
              color: 'var(--gov-text-primary)',
              fontSize: 18,
              width: LAYOUT.headerHeight,
              height: LAYOUT.headerHeight,
              flexShrink: 0,
            }}
          />
        )}

        {/* Logo — compact, no border, matches Nyasha "One" style */}
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            padding: '0 16px',
            fontWeight: 700,
            fontSize: 16,
            fontFamily: 'var(--font-heading)',
            letterSpacing: 0.5,
            color: 'var(--gov-text-primary)',
            flexShrink: 0,
            gap: 8,
          }}
        >
          {/* Logo: env image or colored square fallback */}
          {import.meta.env.VITE_APP_LOGO_URL ? (
            <img
              src={import.meta.env.VITE_APP_LOGO_URL}
              alt={import.meta.env.VITE_APP_NAME ?? 'Logo'}
              style={{ width: 28, height: 28, borderRadius: 6, objectFit: 'contain', flexShrink: 0 }}
            />
          ) : (
            <span style={{
              display: 'inline-block',
              width: 28,
              height: 28,
              borderRadius: 6,
              background: GOV_COLORS.navy,
              flexShrink: 0,
            }} />
          )}
          {layoutMode !== 'mobile' && !siderCollapsed && (
            <div style={{ lineHeight: 1.2 }}>
              <div>{import.meta.env.VITE_APP_NAME ?? 'AqtCoreFW'}</div>
              <div style={{ fontSize: 10, fontWeight: 400, color: 'var(--gov-text-muted)', letterSpacing: 0 }}>
                {import.meta.env.VITE_APP_SUBTITLE ?? 'Hệ thống quản trị — Khung CNTT Chính phủ'}
              </div>
            </div>
          )}
        </div>

        <Topbar layoutMode={layoutMode} onSettingsOpen={() => setSettingsOpen(true)} />
      </header>

      {/* Body below header */}
      <div style={{ display: 'flex', paddingTop: LAYOUT.headerHeight }}>
        {/* FIXED sidebar — independent scroll */}
        {layoutMode !== 'mobile' && (
          <aside
            style={{
              position: 'fixed',
              top: LAYOUT.headerHeight,
              left: 0,
              bottom: 0,
              width: siderWidth,
              background: 'var(--sidebar-bg)',
              borderRight: '1px solid var(--gov-border-color)',
              overflowY: 'auto',
              overflowX: 'hidden',
              transition: 'width var(--transition-normal)',
              zIndex: 50,
            }}
          >
            <SidebarMenu permissions={permissions} roles={roles} menuTree={menuLoading ? undefined : menuTree} popupSubMenus={popupSubMenus} collapsed={collapsed} />

            {/* Collapse toggle — desktop only */}
            {layoutMode === 'desktop' && (
              <div style={{ padding: '8px 12px', borderTop: '1px solid var(--gov-border-color)' }}>
                <Button
                  type="text"
                  size="small"
                  block
                  onClick={() => setCollapsed(!collapsed)}
                  style={{ color: 'var(--gov-text-muted)', fontSize: 12 }}
                >
                  {collapsed ? '→' : '←'}
                </Button>
              </div>
            )}
          </aside>
        )}

        {/* Mobile drawer */}
        {layoutMode === 'mobile' && (
          <MobileNavDrawer
            open={drawerOpen}
            onClose={() => setDrawerOpen(false)}
            permissions={permissions}
            roles={roles}
            menuTree={menuLoading ? undefined : menuTree}
          />
        )}

        {/* Scrollable content area */}
        <main
          id="main-content"
          role="main"
          aria-label="Main content"
          className="gov-page-enter"
          style={{
            flex: 1,
            marginLeft: layoutMode !== 'mobile' ? siderWidth : 0,
            padding: contentPadding,
            minHeight: `calc(100vh - ${LAYOUT.headerHeight}px)`,
            background: 'var(--gov-bg-layout)',
            transition: 'margin-left var(--transition-normal)',
          }}
        >
          <Suspense fallback={<RouteLoadingSpinner />}>
            <Outlet />
          </Suspense>
        </main>
      </div>

      <SettingsDrawer
        open={settingsOpen}
        onClose={() => setSettingsOpen(false)}
        collapsed={collapsed}
        onCollapsedChange={setCollapsed}
        popupSubMenus={popupSubMenus}
        onPopupSubMenusChange={setPopupSubMenus}
      />
      {/* Copilot belongs to the deferred AI module — its floating button/sidebar must not
          render on deployments where AI is off (the endpoints it calls are unmapped → 404).
          CopilotProvider stays: it is inert context state, and gating it would risk breaking
          any descendant that reads the context. */}
      {flags.ai && (
        <>
          <CopilotSidebar />
          <CopilotToggleButton />
        </>
      )}
    </div>
    </CopilotProvider>
  );
}
