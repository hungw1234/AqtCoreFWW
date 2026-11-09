// session-config-api.ts — fetch idle timeout config from BE SystemParameter
// Uses plain apiClient (not useQuery) because AuthProvider renders OUTSIDE QueryClientProvider

import { apiClient } from '@/core/api';

export interface SessionConfig {
  idleTimeoutMinutes: number;
  countdownSeconds: number;
}

const DEFAULTS: SessionConfig = { idleTimeoutMinutes: 30, countdownSeconds: 60 };
let cachedConfig: SessionConfig | null = null;

/** Fetch session config from BE — caches result for 5 min. Returns defaults on error. */
export async function fetchSessionConfig(): Promise<SessionConfig> {
  if (cachedConfig) return cachedConfig;
  try {
    // apiClient interceptor already unwraps { success, data } envelope → res.data IS the config object
    const res = await apiClient.get<SessionConfig>('/session-config');
    cachedConfig = res.data ?? DEFAULTS;
    // Auto-expire cache after 5 min
    setTimeout(() => { cachedConfig = null; }, 5 * 60_000);
    return cachedConfig;
  } catch {
    return DEFAULTS;
  }
}

/** Reset cache — called on logout so next login re-fetches */
export function clearSessionConfigCache() {
  cachedConfig = null;
}
