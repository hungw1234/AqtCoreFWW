import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { rmSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

// These tests cover the invalidate / reverse-lookup additions to e2e/helpers/auth-helper.ts.
// The proxy wrapper in fixtures/test-base.ts is intentionally validated empirically by the
// Tier D e2e suite.

// The helper persists tokens to a FIXED path (e2e/.cache/tokens.json) with no override hook.
// vi.resetModules() resets its in-memory Maps but NOT this file — a token left on disk by a
// prior run (Playwright globalSetup or an earlier test) is a valid 900s-TTL cache hit that
// short-circuits ROPC, so getAccessToken returns the stale token instead of calling our mock.
// Delete the helper's real cache file around each test to guarantee a cold cache.
const HELPER_CACHE_FILE = join(
  dirname(fileURLToPath(import.meta.url)),
  '../../../e2e/.cache/tokens.json',
);

describe('auth-helper — invalidate + reverse lookup', () => {
  let helperModule: typeof import('../../../e2e/helpers/auth-helper');

  beforeEach(async () => {
    vi.resetModules();
    rmSync(HELPER_CACHE_FILE, { force: true });
    helperModule = await import('../../../e2e/helpers/auth-helper');
  });

  it('getRoleForToken returns null for unknown token', () => {
    expect(helperModule.getRoleForToken('totally-unknown-token-' + Date.now())).toBeNull();
  });

  it('getRoleForToken returns the role after a successful ROPC and survives invalidate', async () => {
    const token = 'fake-ropc-token-' + Math.random();
    // Simulate a successful ROPC flow by stub-mocking APIRequestContext.post
    const fakeRequest = {
      post: vi.fn().mockResolvedValue({
        json: () => Promise.resolve({ access_token: token, expires_in: 900 }),
      }),
    } as unknown as Parameters<typeof helperModule.getAccessToken>[0];

    const acquired = await helperModule.getAccessToken(fakeRequest, 'admin');
    expect(acquired).toBe(token);
    expect(helperModule.getRoleForToken(token)).toBe('admin');

    // Invalidate clears the active cache slot but reverse map is intentionally retained so
    // any subsequent 401 on the now-revoked token can still resolve to its role for retry.
    helperModule.invalidateToken('admin');
    expect(helperModule.getRoleForToken(token)).toBe('admin');
  });

  it('forceFresh bypasses both caches and triggers a new ROPC call', async () => {
    const firstToken = 'first-token-' + Math.random();
    const secondToken = 'second-token-' + Math.random();
    const post = vi
      .fn()
      .mockResolvedValueOnce({ json: () => Promise.resolve({ access_token: firstToken, expires_in: 900 }) })
      .mockResolvedValueOnce({ json: () => Promise.resolve({ access_token: secondToken, expires_in: 900 }) });
    const fakeRequest = { post } as unknown as Parameters<typeof helperModule.getAccessToken>[0];

    const a = await helperModule.getAccessToken(fakeRequest, 'admin');
    expect(a).toBe(firstToken);
    expect(post).toHaveBeenCalledTimes(1);

    // Without forceFresh, the cache hit should short-circuit
    const cached = await helperModule.getAccessToken(fakeRequest, 'admin');
    expect(cached).toBe(firstToken);
    expect(post).toHaveBeenCalledTimes(1);

    // With forceFresh, ROPC should fire again and yield the second token
    const refreshed = await helperModule.getAccessToken(fakeRequest, 'admin', { forceFresh: true });
    expect(refreshed).toBe(secondToken);
    expect(post).toHaveBeenCalledTimes(2);
    expect(helperModule.getRoleForToken(secondToken)).toBe('admin');
  });

  afterEach(() => {
    rmSync(HELPER_CACHE_FILE, { force: true });
  });
});
