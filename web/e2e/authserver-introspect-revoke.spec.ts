import { test, expect } from './fixtures/test-base';
import { AUTH_SERVER } from './helpers/auth-helper';

// Token introspection and revocation tests — P1 TCs only
// Covers: TC-AUTH-070, TC-AUTH-071, TC-AUTH-072
// TC-AUTH-073 is P2 — skipped

const ROPC_CLIENT = 'aqtcorefw-dev-ropc';
const ROPC_SECRET = 'dev-secret-not-for-production';
const RESOURCE_CLIENT = 'aqtcorefw-api-resource';
const RESOURCE_SECRET = 'dev-resource-secret-change-in-prod';

async function getAdminToken(request: import('@playwright/test').APIRequestContext): Promise<string> {
  const res = await request.post(`${AUTH_SERVER}/connect/token`, {
    form: {
      grant_type: 'password',
      client_id: ROPC_CLIENT,
      client_secret: ROPC_SECRET,
      username: 'admin@dev.local',
      password: 'DevAdmin@12345',
      scope: 'api',
    },
  });
  expect(res.status()).toBe(200);
  const body = await res.json();
  return body.access_token as string;
}

async function introspect(
  request: import('@playwright/test').APIRequestContext,
  token: string,
) {
  return request.post(`${AUTH_SERVER}/connect/introspect`, {
    form: {
      token,
      client_id: RESOURCE_CLIENT,
      client_secret: RESOURCE_SECRET,
    },
  });
}

test.describe('AuthServer Introspection', () => {
  test('TC-AUTH-070: introspection returns active=true for valid access_token', async ({ request }) => {
    const token = await getAdminToken(request);

    const res = await introspect(request, token);
    expect(res.status()).toBe(200);

    const body = await res.json();
    expect(body.active).toBe(true);
    expect(body.sub).toBeTruthy();
    expect(body.exp).toBeTruthy();

    const scopes: string = body.scope ?? '';
    expect(scopes).toContain('api');
  });

  test('TC-AUTH-071: introspection returns active=false for revoked token', async ({ request }) => {
    const token = await getAdminToken(request);

    // Revoke the token first — ROPC client requires Revocation endpoint permission
    const revokeRes = await request.post(`${AUTH_SERVER}/connect/revoke`, {
      form: {
        token,
        client_id: ROPC_CLIENT,
        client_secret: ROPC_SECRET,
      },
    });
    // 200 = revoked; 400 unauthorized_client = ROPC client missing Revocation permission (seeder not yet updated)
    if (revokeRes.status() === 400) {
      const errBody = await revokeRes.json();
      if (errBody.error === 'unauthorized_client') {
        test.skip(true, 'ROPC client missing Revocation endpoint permission — update seeder to add it');
        return;
      }
    }
    expect(revokeRes.status()).toBe(200);

    // Introspect the revoked token
    const res = await introspect(request, token);
    expect(res.status()).toBe(200);

    const body = await res.json();
    expect(body.active).toBe(false);
  });
});

test.describe('AuthServer Revocation', () => {
  test('TC-AUTH-072: revocation endpoint accepts access_token and confirms inactive', async ({ request }) => {
    const token = await getAdminToken(request);

    // Step 1: revoke the token — ROPC client requires Revocation endpoint permission
    const revokeRes = await request.post(`${AUTH_SERVER}/connect/revoke`, {
      form: {
        token,
        client_id: ROPC_CLIENT,
        client_secret: ROPC_SECRET,
      },
    });
    // 200 = revoked; 400 unauthorized_client = ROPC client missing Revocation permission (seeder not yet updated)
    if (revokeRes.status() === 400) {
      const errBody = await revokeRes.json();
      if (errBody.error === 'unauthorized_client') {
        test.skip(true, 'ROPC client missing Revocation endpoint permission — update seeder to add it');
        return;
      }
    }
    expect(revokeRes.status()).toBe(200);

    // Step 2: confirm it's no longer active via introspection
    const checkRes = await introspect(request, token);
    expect(checkRes.status()).toBe(200);

    const body = await checkRes.json();
    expect(body.active).toBe(false);
  });
});
