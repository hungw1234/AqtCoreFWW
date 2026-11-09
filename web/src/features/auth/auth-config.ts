import { WebStorageStateStore } from 'oidc-client-ts';

// OIDC configuration for aqtcorefw-spa-dev — PKCE flow, no client secret
// sessionStorage: tokens survive HMR/tab refresh but cleared on tab close (secure enough for admin app)
export const oidcConfig = {
  authority: import.meta.env.VITE_OIDC_AUTHORITY ?? 'http://localhost:5002',
  client_id: 'aqtcorefw-spa-dev',
  redirect_uri: `${window.location.origin}/callback`,
  post_logout_redirect_uri: window.location.origin,
  response_type: 'code',
  scope: 'openid profile email roles api offline_access',
  automaticSilentRenew: true,
  // Trigger silent renew 3 min before expiry (default 60s was too late — warning showed before renew)
  accessTokenExpiringNotificationTimeInSeconds: 180,
  silent_redirect_uri: `${window.location.origin}/silent-renew.html`,
  userStore: new WebStorageStateStore({ store: sessionStorage }),
} as const;
