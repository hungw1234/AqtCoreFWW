import { useEffect } from 'react';
import { ConfigProvider, App as AntApp, theme } from 'antd';
import { QueryClientProvider } from '@tanstack/react-query';
import enUS from 'antd/locale/en_US';
import viVN from 'antd/locale/vi_VN';
import { useTranslation } from 'react-i18next';
import { govTheme, GOV_DARK_COLORS } from './theme';
import { AuthProvider } from '@/features/auth';
import { queryClient } from '@/core/api';
import { setNotificationInstance, setMessageInstance } from '@/core/api/notification-bridge';
import '@/core/i18n';
import { ErrorBoundary } from '@/shared/components/error-boundary';
import { ThemeModeProvider, useThemeMode } from '@/core/theme/use-theme-mode';
import type { ReactNode } from 'react';

// Bridge: syncs App-scoped notification AND message singletons for non-React code
// (query-config) and the appMessage facade used by 70+ feature consumers in lieu of
// the antd v5-deprecated static `message.*` API.
function NotificationBridge() {
  const { notification, message } = AntApp.useApp();
  useEffect(() => { setNotificationInstance(notification); }, [notification]);
  useEffect(() => { setMessageInstance(message); }, [message]);
  return null;
}

interface ProvidersProps {
  children: ReactNode;
}

// Inner component: reads theme mode AFTER ThemeModeProvider is mounted in the tree.
// This ensures useThemeMode() reads from the live context, not the default { isDark: false }.
function InnerProviders({ children }: ProvidersProps) {
  const { i18n } = useTranslation();
  const { isDark } = useThemeMode();
  const antLocale = i18n.language === 'en' ? enUS : viVN;
  const algorithm = isDark ? theme.darkAlgorithm : theme.defaultAlgorithm;

  // Dark mode: override surface/text/layout tokens so Ant components flip properly
  const darkTokenOverrides = isDark ? {
    token: {
      colorBgLayout: GOV_DARK_COLORS.bgLayout,
      colorBgContainer: GOV_DARK_COLORS.bgCard,
      colorText: GOV_DARK_COLORS.textPrimary,
      colorTextSecondary: GOV_DARK_COLORS.textSecondary,
      colorBorder: GOV_DARK_COLORS.borderColor,
    },
    components: {
      Layout: {
        headerBg: GOV_DARK_COLORS.bgCard,
        siderBg: GOV_DARK_COLORS.navyDeep,
      },
      Menu: {
        darkItemBg: 'transparent',
        darkSubMenuItemBg: 'rgba(255,255,255,0.04)',
        darkItemSelectedBg: 'rgba(0,123,255,0.25)',
        darkItemHoverBg: 'rgba(255,255,255,0.08)',
        darkItemSelectedColor: '#3395FF',
      },
    },
  } : {};

  // Sync data-theme attribute on <html> for CSS custom property dark mode overrides
  useEffect(() => {
    document.documentElement.setAttribute('data-theme', isDark ? 'dark' : 'light');
  }, [isDark]);

  return (
    <AuthProvider>
      <QueryClientProvider client={queryClient}>
        <ConfigProvider theme={{ ...govTheme, ...darkTokenOverrides, algorithm }} locale={antLocale}>
          <AntApp>
            <NotificationBridge />
            {children}
          </AntApp>
        </ConfigProvider>
      </QueryClientProvider>
    </AuthProvider>
  );
}

// Wraps the app with ErrorBoundary, ThemeModeProvider, AuthProvider,
// Ant Design (GOV theme + dynamic locale) and React Query.
// ThemeModeProvider must be an ancestor of InnerProviders so useThemeMode()
// reads actual state (not the default false value).
export function Providers({ children }: ProvidersProps) {
  return (
    <ErrorBoundary>
      <ThemeModeProvider>
        <InnerProviders>{children}</InnerProviders>
      </ThemeModeProvider>
    </ErrorBoundary>
  );
}
