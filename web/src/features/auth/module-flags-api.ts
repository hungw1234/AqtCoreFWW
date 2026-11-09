// Deferred-module flags API hook — which of the five v1.1-deferred modules
// (AI, Collaboration, BusinessProcess, Reporting, Rules) are enabled in THIS deployment.
// Deploy-time configuration, not per-user state: fetched once and cached for the session.
// The real enforcement is server-side (a disabled module's endpoints are not mapped → 404);
// this hook only drives UX — hiding menus and routes the API would reject anyway.

import { useQuery } from '@tanstack/react-query';
import { apiClient } from '@/core/api';

// ─── Types ────────────────────────────────────────────────────────────────────

export interface ModuleFlagsDto {
  ai: boolean;
  collaboration: boolean;
  businessProcess: boolean;
  reporting: boolean;
  rules: boolean;
}

export type DeferredModuleName = keyof ModuleFlagsDto;

// Fail-open default: if the flags cannot be fetched (network glitch, transient 5xx),
// render as if everything is enabled — the BE 404 is the actual gate, and failing
// closed here would blank out five features over a hiccup.
export const ALL_MODULES_ENABLED: ModuleFlagsDto = {
  ai: true,
  collaboration: true,
  businessProcess: true,
  reporting: true,
  rules: true,
};

// ─── Query keys ───────────────────────────────────────────────────────────────

export const moduleFlagsQueryKeys = {
  flags: ['system', 'module-flags'] as const,
};

// ─── Query ────────────────────────────────────────────────────────────────────

/**
 * GET /api/v1/system/module-flags — enabled state per deferred module.
 * staleTime Infinity: the values only change on redeploy; a page refresh refetches.
 */
export function useModuleFlags() {
  const query = useQuery({
    queryKey: moduleFlagsQueryKeys.flags,
    queryFn: () =>
      apiClient.get<ModuleFlagsDto>('/system/module-flags').then((r) => r.data),
    staleTime: Infinity,
    retry: 1,
  });

  return {
    // Fail-open while loading and on error (see ALL_MODULES_ENABLED rationale).
    flags: query.data ?? ALL_MODULES_ENABLED,
    isFlagsLoading: query.isLoading,
  };
}
