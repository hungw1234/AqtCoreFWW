import { test as base, expect, type APIRequestContext, type APIResponse } from '@playwright/test';
import {
  getAccessToken,
  getRoleForToken,
  invalidateToken,
  type AuthRole,
} from '../helpers/auth-helper';

/**
 * Extended Playwright `test` whose `request` fixture transparently retries any 401 response
 * whose Authorization Bearer token was sourced from `auth-helper.getAccessToken`. The retry
 * invalidates the cached token (in-process Map + filesystem cache), re-acquires via ROPC, and
 * replays the request once. Non-401 responses, intentional 401s on tokens we never issued, and
 * second consecutive 401s are surfaced untouched.
 *
 * Rationale: see PHASE7-TIER-D-TOKEN-REVOKE-INTRA-SUITE. BE handlers
 * (ResetPasswordCommandHandler, AssignRoleCommandHandler, SyncUserRolesCommandHandler,
 * SessionAdminController.RevokeAllForUser) call `RevokeTokenCommand(UserId)` which nukes ALL
 * reference tokens for the target subject. Specs select target via
 * `GET /admin/users?page=1&pageSize=1` (Dapper ORDER BY CreatedAtUtc DESC), so admin@dev.local
 * lands at the head of the list early in the suite and gets revoked. The wrapper makes that
 * recoverable without per-spec changes.
 */

const VERBS_TO_WRAP: ReadonlyArray<keyof APIRequestContext> = [
  'get',
  'post',
  'put',
  'delete',
  'patch',
  'head',
  'fetch',
];

type RequestOptions = Parameters<APIRequestContext['get']>[1];

function extractBearerToken(options: RequestOptions | undefined): string | null {
  const headers = options?.headers;
  if (!headers) return null;
  const auth =
    (headers as Record<string, string>).Authorization ??
    (headers as Record<string, string>).authorization;
  if (typeof auth !== 'string' || !auth.toLowerCase().startsWith('bearer ')) {
    return null;
  }
  return auth.slice(7).trim();
}

function wrapRequestWithRetry(ctx: APIRequestContext): APIRequestContext {
  const wrapped = new Proxy(ctx, {
    get(target, prop, receiver) {
      const original = Reflect.get(target, prop, receiver);
      if (typeof original !== 'function') return original;

      if (!VERBS_TO_WRAP.includes(prop as keyof APIRequestContext)) {
        return original.bind(target);
      }

      return async (...args: unknown[]): Promise<APIResponse> => {
        const res = (await original.apply(target, args)) as APIResponse;
        if (res.status() !== 401) return res;

        // Token-match gate: only retry when the request carried a Bearer header sourced from
        // auth-helper. Raw fetches (no Bearer) and intentional 401 tests (Bearer on tokens we
        // never issued) pass through untouched.
        const options = args[1] as RequestOptions | undefined;
        const token = extractBearerToken(options);
        if (!token) return res;

        const role = getRoleForToken(token);
        if (!role) return res;

        invalidateToken(role);
        let fresh: string;
        try {
          fresh = await getAccessToken(target, role, { forceFresh: true });
        } catch (err) {
          console.warn(`[auth-aware-request] retry abandoned: forceFresh ROPC failed: ${String(err)}`);
          return res;
        }

        const url = args[0] as string;
        // Caveat: if `options.multipart` carries a readable stream (e.g. fs.createReadStream),
        // this shallow merge will replay an already-consumed stream — use Buffer-backed payloads.
        const retryOptions: RequestOptions = {
          ...(options ?? {}),
          headers: {
            ...((options?.headers as Record<string, string>) ?? {}),
            Authorization: `Bearer ${fresh}`,
          },
        };
        console.warn(`[auth-aware-request] 401 on cached "${role}" token — retrying ${String(prop)} ${url} once with fresh token`);
        return (await original.call(target, url, retryOptions)) as APIResponse;
      };
    },
  });
  return wrapped;
}

export const test = base.extend({
  request: async ({ request }, use) => {
    await use(wrapRequestWithRetry(request));
  },
});

export { expect };
export { request } from '@playwright/test';
export type {
  Page,
  Locator,
  Browser,
  BrowserContext,
  APIRequestContext,
  APIResponse,
  TestInfo,
} from '@playwright/test';
export type { AuthRole };
