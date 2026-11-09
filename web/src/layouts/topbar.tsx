import { useCallback } from 'react';
import { Avatar, Button, Dropdown, Space, Typography, Tooltip } from 'antd';
import {
  LogoutOutlined,
  ExpandOutlined,
  CompressOutlined,
  SafetyCertificateOutlined,
  SettingOutlined,
  UserOutlined,
} from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { useNavigate } from '@tanstack/react-router';
import { useAuth } from '@/features/auth';
import { ThemeSwitcher } from '@/core/theme/theme-switcher';
import { AdminTenantSelector } from '@/shared/components/admin-tenant-selector';
import { NotificationBell } from '@/features/notifications';
import { GOV_COLORS } from '@/app/theme';
import { getInitials } from '@/shared/utils';
import type { MenuProps } from 'antd';
import type { LayoutMode } from '@/core/hooks/use-layout-mode';

// Header icon button — consistent styling for all header actions
function HeaderIconButton({ icon, tooltip, onClick, badge }: {
  icon: React.ReactNode;
  tooltip: string;
  onClick?: () => void;
  badge?: number;
}) {
  return (
    <Tooltip title={tooltip}>
      <Button
        type="text"
        icon={icon}
        onClick={onClick}
        style={{
          color: 'var(--gov-text-secondary)',
          fontSize: 18,
          width: 36,
          height: 36,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          position: 'relative',
        }}
      />
      {badge != null && badge > 0 && (
        <span style={{
          position: 'absolute',
          top: -2,
          right: -2,
          background: '#DC3545',
          color: '#fff',
          fontSize: 10,
          fontWeight: 600,
          borderRadius: 10,
          padding: '0 5px',
          minWidth: 16,
          height: 16,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          lineHeight: 1,
        }}>
          {badge > 99 ? '99+' : badge}
        </span>
      )}
    </Tooltip>
  );
}

interface TopbarProps {
  layoutMode?: LayoutMode;
  onSettingsOpen?: () => void;
}

// Topbar: Nyasha-style header controls — fullscreen, theme, settings, bell, avatar
export function Topbar({ layoutMode = 'desktop', onSettingsOpen }: TopbarProps) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { user, logout } = useAuth();

  const displayName = user?.profile?.name ?? user?.profile?.sub ?? 'Admin';
  const initials = getInitials(String(displayName));
  const isMobile = layoutMode === 'mobile';

  const toggleFullscreen = useCallback(() => {
    if (document.fullscreenElement) {
      document.exitFullscreen();
    } else {
      document.documentElement.requestFullscreen();
    }
  }, []);

  const userMenuItems: MenuProps['items'] = [
    {
      key: 'profile',
      icon: <UserOutlined />,
      label: t('nav.profile'),
      onClick: () => navigate({ to: '/profile' }),
    },
    {
      key: 'mfa',
      icon: <SafetyCertificateOutlined />,
      label: t('nav.mfaEnrolment', 'Bảo mật MFA'),
      onClick: () => navigate({ to: '/account/mfa' }),
    },
    {
      key: 'settings',
      icon: <SettingOutlined />,
      label: t('nav.settings', { defaultValue: 'Cài đặt' }),
      onClick: () => onSettingsOpen?.(),
    },
    { type: 'divider' },
    {
      key: 'logout',
      icon: <LogoutOutlined />,
      label: t('auth.logout'),
      danger: true,
      onClick: () => logout(),
    },
  ];

  return (
    <div
      style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'flex-end',
        flex: 1,
        height: '100%',
        padding: isMobile ? '0 12px' : '0 20px',
      }}
    >
      <Space size={isMobile ? 4 : 8} align="center">
        {/* SystemAdmin cross-tenant selector */}
        {!isMobile && <AdminTenantSelector />}

        {/* Fullscreen toggle — desktop only */}
        {!isMobile && (
          <HeaderIconButton
            icon={document.fullscreenElement ? <CompressOutlined /> : <ExpandOutlined />}
            tooltip={t('common.fullscreen', { defaultValue: 'Toàn màn hình' })}
            onClick={toggleFullscreen}
          />
        )}

        {/* Theme toggle (dark/light) */}
        <ThemeSwitcher />

        {/* Settings drawer trigger */}
        {!isMobile && (
          <HeaderIconButton
            icon={<SettingOutlined />}
            tooltip={t('nav.settings', { defaultValue: 'Cài đặt' })}
            onClick={onSettingsOpen}
          />
        )}

        {/* Notification bell — popover with recent items + realtime unread badge */}
        <NotificationBell />

        {/* User avatar + dropdown */}
        <Dropdown menu={{ items: userMenuItems }} placement="bottomRight" arrow>
          <button
            type="button"
            aria-label={`User menu for ${displayName}`}
            aria-haspopup="true"
            style={{
              cursor: 'pointer',
              color: 'var(--gov-text-primary)',
              border: 'none',
              background: 'none',
              display: 'flex',
              alignItems: 'center',
              gap: 8,
              padding: '4px 0 4px 8px',
            }}
          >
            <Avatar
              size={32}
              style={{
                background: GOV_COLORS.navy,
                color: GOV_COLORS.white,
                fontWeight: 600,
                fontSize: 12,
              }}
            >
              {initials}
            </Avatar>
            {!isMobile && (
              <Typography.Text style={{ color: 'var(--gov-text-primary)', fontSize: 13 }}>
                {displayName}
              </Typography.Text>
            )}
          </button>
        </Dropdown>
      </Space>
    </div>
  );
}
