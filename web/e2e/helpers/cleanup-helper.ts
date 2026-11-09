import { APIRequestContext } from '@playwright/test';
import { authHeaders, API_BASE } from './auth-helper';

/**
 * Delete a single entity via API with retry on 5xx errors.
 * Fails fast on 4xx (entity already gone or invalid request — no point retrying).
 */
export async function cleanupEntity(
  request: APIRequestContext,
  token: string,
  deletePath: string,
  maxRetries = 2,
): Promise<void> {
  const url = deletePath.startsWith('http') ? deletePath : `${API_BASE}${deletePath}`;

  for (let attempt = 0; attempt <= maxRetries; attempt++) {
    try {
      const res = await request.delete(url, { headers: authHeaders(token) });
      const status = res.status();

      // Success or already deleted (404) — done
      if (status < 400 || status === 404) return;

      // 4xx (except 404) — fail fast, no retry
      if (status >= 400 && status < 500) {
        console.warn(`[cleanup] ${deletePath} → ${status} (skipped, no retry)`);
        return;
      }

      // 5xx — retry if attempts remain
      if (attempt < maxRetries) {
        console.warn(`[cleanup] ${deletePath} → ${status}, retrying (${attempt + 1}/${maxRetries})`);
        await delay(500 * (attempt + 1));
      } else {
        console.warn(`[cleanup] ${deletePath} → ${status} after ${maxRetries + 1} attempts (gave up)`);
      }
    } catch (err) {
      if (attempt < maxRetries) {
        await delay(500 * (attempt + 1));
      } else {
        console.warn(`[cleanup] ${deletePath} failed: ${err}`);
      }
    }
  }
}

/**
 * Delete multiple entities in reverse order (respects FK constraints).
 * Each path is a relative API path like '/api/v1/admin/users/{id}'.
 */
export async function cleanupEntities(
  request: APIRequestContext,
  token: string,
  deletePaths: string[],
): Promise<void> {
  // Reverse order: last created = first deleted (FK safety)
  for (const path of [...deletePaths].reverse()) {
    await cleanupEntity(request, token, path);
  }
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
