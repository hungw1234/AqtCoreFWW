import { useEffect, useRef } from 'react';
import { Spin } from 'antd';
import { userManager } from './auth-provider';

/**
 * CallbackPage: exchanges OIDC authorization code for tokens.
 *
 * Root cause of redirect loop: After signinRedirectCallback() stores the user,
 * navigate() fires before AuthProvider's userLoaded event triggers setUser().
 * RouteGuard reads stale isAuthenticated=false → redirects to /login.
 *
 * Fix: Use window.location.replace() instead of React Router navigate().
 * This forces a full page reload, which re-runs AuthProvider.getUser()
 * and correctly loads the stored user. Simple, reliable, no race conditions.
 */
export function CallbackPage() {
  const processed = useRef(false);

  useEffect(() => {
    if (processed.current) return;
    processed.current = true;

    userManager
      .signinRedirectCallback()
      .then((user) => {
        const raw = (user.state as { returnUrl?: string } | undefined)
          ?.returnUrl ?? '/';
        // Enforce relative path only — blocks open redirect (absolute URLs, protocol-relative, javascript:)
        const returnUrl = (raw.startsWith('/') && !raw.startsWith('//')) ? raw : '/';
        // Full page reload instead of SPA navigate — eliminates race condition
        // between signinRedirectCallback and AuthProvider state update.
        window.location.replace(returnUrl);
      })
      .catch((err) => {
        console.error('[Auth] Callback error:', err);
        window.location.replace('/login');
      });
  }, []);

  return <Spin size="large" fullscreen />;
}
