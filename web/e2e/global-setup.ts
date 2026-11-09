import { request, type FullConfig } from '@playwright/test';
import { getAccessToken, type AuthRole } from './helpers/auth-helper';

/**
 * Playwright globalSetup — Phase-01 token pre-warm + Phase-02 readiness probe.
 *
 * Readiness probe: polls AuthServer / API / FE endpoints before any spec runs.
 * Fail-fast: if any host is unreachable after 60s, setup throws with a clear message
 * so developers know which service to start (instead of cryptic timeout mid-spec).
 *
 * Token pre-warm: ROPC tokens for all 3 shared roles cached to e2e/.cache/tokens.json
 * so every worker reads from disk on first in-process miss instead of hitting
 * AuthServer 5002 in parallel.
 *
 * Error strategy: catch per-role so a single auth failure doesn't block the
 * whole suite; per-spec retry will surface real issues.
 */

// ── Readiness probe ───────────────────────────────────────────────────────────

/**
 * Poll `url` until it responds with HTTP < 500, or throw after `timeoutMs`.
 * Uses Node's global fetch (Node 18+, available in all Playwright runtimes).
 */
async function waitFor(url: string, label: string, timeoutMs = 60_000): Promise<void> {
  const start = Date.now();
  while (Date.now() - start < timeoutMs) {
    try {
      const r = await fetch(url);
      if (r.status < 500) return; // any non-5xx response = host is up
    } catch {
      // Connection refused / DNS error — host not yet listening; retry
    }
    await new Promise<void>((resolve) => setTimeout(resolve, 1000));
  }
  throw new Error(
    `[globalSetup] ${label} not ready at ${url} after ${timeoutMs}ms — ` +
      'ensure the service is running before starting Playwright.',
  );
}

// ── Token pre-warm ────────────────────────────────────────────────────────────

const ROLES: AuthRole[] = ['admin', 'user', 'officer'];

// eslint-disable-next-line @typescript-eslint/no-unused-vars
export default async function globalSetup(_config: FullConfig): Promise<void> {
  // Phase-02: probe all three hosts before wasting time on token requests.
  // Order: AuthServer first (tokens depend on it), then API, then FE.
  // Use 'localhost' (resolves to ::1 or 127.0.0.1 per OS default) — Vite binds IPv6-only on Windows.
  await waitFor('http://localhost:5002/.well-known/openid-configuration', 'AuthServer');
  await waitFor('http://localhost:5001/health/ready', 'API');
  await waitFor('http://localhost:5173', 'FE');

  console.log('[globalSetup] All hosts ready — pre-warming tokens');

  // Phase-01: pre-warm tokens for the 3 shared roles
  const ctx = await request.newContext();
  try {
    for (const role of ROLES) {
      try {
        await getAccessToken(ctx, role);
        console.log(`[globalSetup] Token acquired: ${role}`);
      } catch (err) {
        // Non-fatal — per-spec ROPC retry will surface the real error.
        console.warn(`[globalSetup] Warning: failed to pre-warm token for role "${role}": ${String(err)}`);
      }
    }
  } finally {
    await ctx.dispose();
  }
}
