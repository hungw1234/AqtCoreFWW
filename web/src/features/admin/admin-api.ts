// Admin feature API hooks — health check and system diagnostics

import { useQuery, useQueryClient } from '@tanstack/react-query';

// ─── Types ────────────────────────────────────────────────────────────────────

export interface ServiceHealth {
  name: string;
  url: string;
  status: 'healthy' | 'unhealthy';
  responseTime: number;
}

// ─── Service Definitions ──────────────────────────────────────────────────────

// Absolute URLs via env so the page works on split-domain deployments
// (FE host ≠ API host). Empty fallback = relative → Vite proxy in local dev.
// See ADR-008 for the same-origin reverse-proxy alternative.
const API_BASE = import.meta.env.VITE_API_BASE_URL ?? '';
const AUTH_BASE = import.meta.env.VITE_OIDC_AUTHORITY ?? 'http://localhost:5000';

const HEALTH_SERVICES = [
  { name: 'API (nginx)', url: `${API_BASE}/api/v1/feature-flags/EnablePdfWatermark` },
  { name: 'AuthServer', url: `${AUTH_BASE}/health` },
  { name: 'OpenAPI Spec', url: `${API_BASE}/openapi/v1.json` },
] as const;

// ─── Health Check Logic ───────────────────────────────────────────────────────

async function checkServiceHealth(url: string): Promise<{ ok: boolean; ms: number }> {
  const start = performance.now();
  try {
    const res = await fetch(url, { signal: AbortSignal.timeout(5000) });
    return { ok: res.ok, ms: Math.round(performance.now() - start) };
  } catch {
    return { ok: false, ms: Math.round(performance.now() - start) };
  }
}

async function checkAllServices(): Promise<ServiceHealth[]> {
  return Promise.all(
    HEALTH_SERVICES.map(async (svc) => {
      const { ok, ms } = await checkServiceHealth(svc.url);
      return { ...svc, status: ok ? 'healthy' : 'unhealthy', responseTime: ms } as ServiceHealth;
    }),
  );
}

// ─── Hooks ────────────────────────────────────────────────────────────────────

/** Fetches health status for all backend services. Manual refetch via invalidation. */
export function useHealthCheck() {
  const qc = useQueryClient();
  const query = useQuery({
    queryKey: ['admin', 'health-check'],
    queryFn: checkAllServices,
    staleTime: 0, // always re-fetch on mount
    refetchOnWindowFocus: false,
  });

  const recheck = () => qc.invalidateQueries({ queryKey: ['admin', 'health-check'] });

  return { ...query, recheck };
}
