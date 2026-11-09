import { createContext, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { UserManager, type User } from 'oidc-client-ts';
import { Button } from 'antd';
import { useTranslation } from 'react-i18next';
import { oidcConfig } from './auth-config';
import { stopSignalR } from '@/features/notifications/notification-signalr';
import { fetchSessionConfig, clearSessionConfigCache, type SessionConfig } from './session-config-api';
import { useIdleTimeout } from './use-idle-timeout';
import { SessionExpiryModal } from './session-expiry-modal';
import { setSessionErrorHandler, cleanupAndRedirect } from './session-error-handler';
import { getNotificationInstance } from '@/core/api/notification-bridge';
import {
  createAuthFailureController,
  AUTH_FAILURE_TOAST_KEY,
} from './auth-failure-controller';
import type { ReactNode } from 'react';

export interface AuthContextValue {
  user: User | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  login: () => Promise<void>;
  logout: () => Promise<void>;
}

export const AuthContext = createContext<AuthContextValue | null>(null);

// Singleton UserManager — created once per app lifetime
const userManager = new UserManager(oidcConfig);

interface AuthProviderProps {
  children: ReactNode;
}

// AuthProvider: initialises OIDC session, exposes user state + actions via context.
// Idle timeout → modal with countdown (user can still extend).
// Silent renew failure → toast with 10s countdown then auto-redirect (no modal, no manual click needed).
export function AuthProvider({ children }: AuthProviderProps) {
  const { t } = useTranslation();
  const [user, setUser] = useState<User | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const initialised = useRef(false);
  const isAuthenticated = !!user && !user.expired;

  // --- Idle-only modal state (silent renew failure uses toast countdown instead) ---
  const [modalOpen, setModalOpen] = useState(false);

  const showIdleModal = useCallback(() => setModalOpen(true), []);

  // --- Auth failure countdown → auto-redirect ---
  // Pure controller (see auth-failure-controller.ts) owns guards + timer; AuthProvider
  // only wires the UI (antd toast + i18n) and OIDC calls as injected deps.
  const authFailureController = useMemo(
    () => createAuthFailureController({
      renderToast: (remaining, onClickRedirectNow) => {
        const n = getNotificationInstance();
        if (!n) return;
        n.error({
          key: AUTH_FAILURE_TOAST_KEY,
          message: t('auth.session.expiredTitle', 'Phiên đã hết hạn'),
          description: t('auth.session.expiredCountdown', {
            seconds: remaining,
            defaultValue: 'Không thể gia hạn phiên. Tự động chuyển hướng đăng nhập sau {{seconds}}s.',
          }),
          duration: 0, // persistent — lifecycle owned by countdown, not antd auto-close
          btn: (
            <Button size="small" type="primary" danger onClick={onClickRedirectNow}>
              {t('auth.session.reLoginNow', 'Đăng nhập ngay')}
            </Button>
          ),
        });
      },
      destroyToast: () => getNotificationInstance()?.destroy(AUTH_FAILURE_TOAST_KEY),
      stopSignalR: () => stopSignalR(),
      signinRedirect: () => userManager.signinRedirect(),
    }),
    [t],
  );

  const handleAuthFailure = useCallback(() => authFailureController.trigger(), [authFailureController]);

  // --- OIDC event handlers ---
  useEffect(() => {
    if (initialised.current) return;
    initialised.current = true;

    userManager.getUser().then((u) => {
      setUser(u);
      setIsLoading(false);
    });

    // QĐ 742 1.6.b — token-driven MFA enrolment redirect.
    // BE emits `mfa_enrolment_required` claim (ClaimValueTypes.Boolean → JSON `true`,
    // but accept legacy string `"true"` for resilience) when policy mandates enrolment
    // and grace expired. Defense-in-depth: api-client.ts:175-179 still handles
    // `X-Mfa-Required` 403 header path for stale-session and BE flag-off scenarios.
    const onUserLoaded = (u: User) => {
      setUser(u);
      const claim = u.profile?.['mfa_enrolment_required'];
      const requiresEnrolment = claim === true || claim === 'true';
      if (requiresEnrolment && !window.location.pathname.startsWith('/account/mfa')) {
        window.location.replace('/account/mfa?reason=mfa-enrolment-required');
      }
    };
    // H-05: Stop SignalR on session expiry/unload
    const onUserUnloaded = () => {
      setUser(null);
      stopSignalR().catch(() => {});
    };
    const onSilentRenewError = (err: Error) => {
      console.error('[Auth] Silent renew error:', err);
      handleAuthFailure();
    };

    userManager.events.addUserLoaded(onUserLoaded);
    userManager.events.addUserUnloaded(onUserUnloaded);
    userManager.events.addSilentRenewError(onSilentRenewError);

    // Bridge api-client 401 retry failure → countdown toast
    setSessionErrorHandler(handleAuthFailure);

    return () => {
      userManager.events.removeUserLoaded(onUserLoaded);
      userManager.events.removeUserUnloaded(onUserUnloaded);
      userManager.events.removeSilentRenewError(onSilentRenewError);
      setSessionErrorHandler(null);
      authFailureController.dispose();
    };
  }, [handleAuthFailure, authFailureController]);

  // --- Idle timeout with cross-tab sync ---
  const [sessionConfig, setSessionConfig] = useState<SessionConfig | null>(null);
  useEffect(() => {
    if (!isAuthenticated) return;
    fetchSessionConfig().then(setSessionConfig);
  }, [isAuthenticated]);
  const idleTimeoutMs = (sessionConfig?.idleTimeoutMinutes ?? 30) * 60 * 1000;
  const countdownSeconds = sessionConfig?.countdownSeconds ?? 60;

  const handleTimeout = useCallback(async () => {
    setModalOpen(false);
    await stopSignalR();
    await userManager.signoutRedirect();
  }, []);

  const { resetActivity } = useIdleTimeout({
    idleTimeoutMs,
    onWarning: showIdleModal,
    onTimeout: handleTimeout,
    enabled: isAuthenticated,
  });

  // Extend: reset idle timer + renew token + close modal
  // Renew failure (e.g. invalid_grant) → cleanup stale user + trigger countdown
  // toast so user gets feedback instead of staying stuck on idle modal close.
  const handleExtend = useCallback(() => {
    setModalOpen(false);
    resetActivity();
    userManager.signinSilent().catch(async (err) => {
      console.error('[Auth] Silent renew on extend failed:', err);
      await cleanupAndRedirect();
    });
  }, [resetActivity]);

  const login = useCallback(async () => {
    await userManager.signinRedirect();
  }, []);

  const logout = useCallback(async () => {
    clearSessionConfigCache();
    await stopSignalR();
    await userManager.signoutRedirect();
  }, []);

  return (
    <AuthContext.Provider
      value={{ user, isAuthenticated, isLoading, login, logout }}
    >
      {children}
      <SessionExpiryModal
        open={modalOpen}
        countdownSeconds={countdownSeconds}
        onExtend={handleExtend}
        onTimeout={handleTimeout}
      />
    </AuthContext.Provider>
  );
}

// Export userManager for use in callback/silent-renew pages
export { userManager };
