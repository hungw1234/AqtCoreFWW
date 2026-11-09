import { APIRequestContext } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';
import { fileURLToPath } from 'url';

// Use 127.0.0.1 instead of localhost — Playwright on Windows resolves localhost to ::1 (IPv6)
// which can hang if Docker containers only listen on IPv4
const AUTH_SERVER = process.env.AUTH_SERVER ?? 'http://127.0.0.1:5002';
const API_BASE = process.env.API_BASE ?? 'http://127.0.0.1:5001';

// Supported auth roles for E2E tests — mapped to seeded dev accounts
export type AuthRole = 'admin' | 'user' | 'officer' | 'systemadmin';

// Credentials from E2ETestDataSeeder + OpenIddictDataSeeder
const ROLE_CREDENTIALS: Record<AuthRole, { username: string; password: string }> = {
  admin:       { username: 'admin@dev.local',   password: 'DevAdmin@12345' },
  user:        { username: 'viewer@dev.local',  password: 'DevViewer@12345' },
  officer:     { username: 'officer@dev.local', password: 'DevOfficer@12345' },
  systemadmin: { username: 'admin@dev.local',   password: 'DevAdmin@12345' },
};

// Per-role in-process token cache — avoids redundant ROPC calls within the same worker process
const tokenCache = new Map<AuthRole, { token: string; expiresAt: number }>();

// Reverse lookup token → role. Used by request-fixture override to detect 401-on-our-token
// (vs intentional 401 from raw-fetch tests) and trigger invalidate+retry exactly once.
// Keep stale entries around: a previously-cached-but-now-revoked token still resolves to its
// role here so the wrapper can recover.
const tokenToRole = new Map<string, AuthRole>();

// Proactive refresh threshold: re-acquire when <2min remaining.
// Tightened from 5min so 15min ROPC tokens have a usable cache window (~13min).
const REFRESH_THRESHOLD_MS = 2 * 60 * 1000;
// Fallback used only when ROPC response omits expires_in (shouldn't happen).
// Real TTL derived per-call from `expires_in` (currently 900s/15min from AuthServer).
const FALLBACK_CACHE_MS = 10 * 60 * 1000;

// Cross-worker filesystem cache — shared across all Playwright worker processes
// Resolve relative to this helper file: helpers/ → ../.cache/tokens.json
const _currentDir = (() => {
  try {
    // ESM context (package "type": "module")
    return path.dirname(fileURLToPath(import.meta.url));
  } catch {
    // CJS fallback (Playwright internal compilation wraps in CommonJS)
    return __dirname;
  }
})();

const CACHE_DIR = path.join(_currentDir, '..', '.cache');
const CACHE_FILE = path.join(CACHE_DIR, 'tokens.json');

// Shape of the on-disk cache file
type FileCacheEntry = { token: string; expiresAt: number };
type FileCache = Partial<Record<AuthRole, FileCacheEntry>>;

/** Read the filesystem cache; returns empty object on any error (missing/malformed). */
function readFileCache(): FileCache {
  try {
    const raw = fs.readFileSync(CACHE_FILE, 'utf8');
    return JSON.parse(raw) as FileCache;
  } catch {
    return {};
  }
}

/**
 * Atomic read-modify-write to the shared filesystem cache.
 * Uses process-pid-suffixed .tmp file + rename to avoid clobbering peer workers.
 * Concurrent writes of the same role: last-writer-wins; both tokens are valid,
 * the loser simply re-acquires from the cache or via fresh ROPC on next call.
 */
function writeFileCache(role: AuthRole, entry: FileCacheEntry): void {
  try {
    fs.mkdirSync(CACHE_DIR, { recursive: true });
    const existing = readFileCache();
    const merged: FileCache = { ...existing, [role]: entry };
    const tmp = `${CACHE_FILE}.tmp.${process.pid}`;
    fs.writeFileSync(tmp, JSON.stringify(merged, null, 2), 'utf8');
    fs.renameSync(tmp, CACHE_FILE);
  } catch (err) {
    // Best-effort — workers will simply re-acquire on next miss
    console.warn(`[auth-helper] Warning: failed to write token cache: ${String(err)}`);
  }
}

/** Returns true when a cache entry is still valid (not within the refresh threshold). */
function isValid(entry: FileCacheEntry | undefined): entry is FileCacheEntry {
  return !!entry && Date.now() < entry.expiresAt - REFRESH_THRESHOLD_MS;
}

/**
 * Acquire ROPC access token for E2E API tests (in-process Map → fs cache → ROPC).
 *
 * @param opts.forceFresh When true, bypass both Map and filesystem caches and always perform ROPC.
 *   Used by `auth-aware-request` fixture after a 401 response on a previously-cached token to
 *   recover from server-side revocation (see PHASE7-TIER-D-TOKEN-REVOKE-INTRA-SUITE).
 */
export async function getAccessToken(
  request: APIRequestContext,
  role: AuthRole = 'admin',
  opts: { forceFresh?: boolean } = {},
): Promise<string> {
  if (!opts.forceFresh) {
    // Fast path: in-process Map (same worker, already resolved)
    const inMem = tokenCache.get(role);
    if (isValid(inMem)) {
      return inMem.token;
    }

    // Cross-worker path: read shared filesystem cache written by globalSetup or peer workers
    const fileCache = readFileCache();
    const fromFile = fileCache[role];
    if (isValid(fromFile)) {
      // Populate in-process cache to skip disk reads on subsequent calls this worker
      tokenCache.set(role, fromFile);
      tokenToRole.set(fromFile.token, role);
      return fromFile.token;
    }
  }

  // Cold / expired / forceFresh: ROPC against AuthServer
  const creds = ROLE_CREDENTIALS[role];
  const response = await request.post(`${AUTH_SERVER}/connect/token`, {
    form: {
      grant_type: 'password',
      client_id: 'aqtcorefw-dev-ropc',
      client_secret: 'dev-secret-not-for-production',
      username: creds.username,
      password: creds.password,
      scope: 'api',
    },
  });
  const body = await response.json() as { access_token?: string; expires_in?: number };
  const token = body.access_token;
  if (!token) {
    throw new Error(`[auth-helper] ROPC failed for role "${role}": ${JSON.stringify(body)}`);
  }

  const ttlMs = typeof body.expires_in === 'number' && body.expires_in > 0
    ? body.expires_in * 1000
    : FALLBACK_CACHE_MS;
  const entry: FileCacheEntry = { token, expiresAt: Date.now() + ttlMs };
  tokenCache.set(role, entry);
  tokenToRole.set(token, role);
  // Best-effort write to cross-worker cache so other workers benefit
  writeFileCache(role, entry);
  return token;
}

/**
 * Invalidate the cached token for a role. Removes the in-process Map entry AND rewrites the
 * filesystem cache without that role's entry. Reverse `tokenToRole` map is INTENTIONALLY kept
 * intact so that a follow-up 401 on the just-invalidated token can still resolve back to a role
 * for retry detection.
 */
export function invalidateToken(role: AuthRole): void {
  tokenCache.delete(role);
  try {
    const existing = readFileCache();
    if (existing[role]) {
      const { [role]: _removed, ...rest } = existing;
      const tmp = `${CACHE_FILE}.tmp.${process.pid}`;
      fs.mkdirSync(CACHE_DIR, { recursive: true });
      fs.writeFileSync(tmp, JSON.stringify(rest, null, 2), 'utf8');
      fs.renameSync(tmp, CACHE_FILE);
    }
  } catch (err) {
    console.warn(`[auth-helper] Warning: failed to invalidate fs cache for role "${role}": ${String(err)}`);
  }
}

/** Reverse-lookup: which role does this Bearer token belong to (if known)? */
export function getRoleForToken(token: string): AuthRole | null {
  return tokenToRole.get(token) ?? null;
}

/** Create auth headers for API requests */
export function authHeaders(token: string) {
  return { Authorization: `Bearer ${token}` };
}

/** Resolve current user ID by calling the users API (works with opaque tokens) */
export async function resolveUserId(request: APIRequestContext, token: string): Promise<string> {
  const res = await request.get(`${API_BASE}/api/v1/admin/users?page=1&pageSize=1`, {
    headers: authHeaders(token),
  });
  if (!res.ok()) return '';
  const body = await res.json() as { data?: { items?: Array<{ id: string }> }; items?: Array<{ id: string }> };
  const items = body.data?.items ?? body.items ?? [];
  return items[0]?.id ?? '';
}

export { AUTH_SERVER, API_BASE };
