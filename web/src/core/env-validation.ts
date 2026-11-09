// Runtime environment variable validation — runs once on app startup.
// Fails fast with clear error if required vars are missing.

interface EnvVar {
  key: string;
  required: boolean;
  defaultValue?: string;
}

const ENV_VARS: EnvVar[] = [
  { key: 'VITE_OIDC_AUTHORITY', required: false, defaultValue: 'http://localhost:5002' },
  { key: 'VITE_AUTH_CLIENT_ID', required: false, defaultValue: 'aqtcorefw-spa-dev' },
  { key: 'VITE_API_BASE_URL', required: false },
  { key: 'VITE_APP_NAME', required: false, defaultValue: 'AqtCoreFW' },
  { key: 'VITE_APP_SUBTITLE', required: false, defaultValue: 'Hệ thống quản trị — Khung CNTT Chính phủ' },
  { key: 'VITE_APP_LOGO_URL', required: false },
];

export function validateEnv(): void {
  const missing: string[] = [];

  for (const { key, required } of ENV_VARS) {
    if (required && !import.meta.env[key]) {
      missing.push(key);
    }
  }

  if (missing.length > 0) {
    const msg = `Missing required environment variables:\n${missing.map(k => `  - ${k}`).join('\n')}\n\nCopy .env.example to .env.local and fill in values.`;
    console.error(msg);
    throw new Error(msg);
  }

  // Log env status in development
  if (import.meta.env.DEV) {
    console.info('[env] Loaded environment:', {
      OIDC: import.meta.env.VITE_OIDC_AUTHORITY || '(default)',
      API: import.meta.env.VITE_API_BASE_URL || '(proxy)',
      App: import.meta.env.VITE_APP_NAME || '(default)',
    });
  }
}
