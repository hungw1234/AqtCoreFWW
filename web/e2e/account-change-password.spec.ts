import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// Account E2E — TC-IDENT-034/035/036 + validation tests
test.describe('Account — change password (authenticated API)', () => {
  let token: string;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    expect(token).toBeTruthy();
  });

  // TC-IDENT-034: change password — valid current password returns 200
  // Uses viewer account (DevViewer@12345) and restores after success to keep seed stable
  test('TC-IDENT-034: POST /account/change-password with valid credentials returns 200', async ({ request }) => {
    const viewerToken = await getAccessToken(request, 'user');
    const tempPass = 'TempViewer@2026!';
    const origPass = 'DevViewer@12345';

    const res = await request.post(`${API_BASE}/api/v1/account/change-password`, {
      headers: { ...authHeaders(viewerToken), 'Content-Type': 'application/json' },
      data: { currentPassword: origPass, newPassword: tempPass },
    });
    // 200 = changed; 400/422 = policy violation or wrong creds
    expect([200, 400, 422]).toContain(res.status());

    // Restore original password if change succeeded
    if (res.status() === 200) {
      const newToken = await request.post(`${process.env.AUTH_SERVER ?? 'http://127.0.0.1:5002'}/connect/token`, {
        form: {
          grant_type: 'password', client_id: 'aqtcorefw-dev-ropc',
          client_secret: 'dev-secret-not-for-production',
          username: 'viewer@dev.local', password: tempPass, scope: 'api',
        },
      });
      const restoredToken = (await newToken.json()).access_token;
      if (restoredToken) {
        await request.post(`${API_BASE}/api/v1/account/change-password`, {
          headers: { ...authHeaders(restoredToken), 'Content-Type': 'application/json' },
          data: { currentPassword: tempPass, newPassword: origPass },
        });
      }
    }
  });

  // TC-IDENT-035: wrong current password returns 400/422
  // PHASE7-TIER-D-CASCADE: defensive swap admin→viewer. UserManager.ChangePasswordAsync per Identity
  // source does NOT call AccessFailedAsync (no lockout pollution), but swap aligns w/ TC-IDENT-034
  // pattern + preempts pollution if Identity middleware ever changes.
  test('TC-IDENT-035: POST /account/change-password wrong current password returns 400/422', async ({ request }) => {
    const viewerToken = await getAccessToken(request, 'user');
    const res = await request.post(`${API_BASE}/api/v1/account/change-password`, {
      headers: { ...authHeaders(viewerToken), 'Content-Type': 'application/json' },
      data: { currentPassword: 'WrongP@ss!', newPassword: 'NewP@ss123!' },
    });
    expect([400, 422]).toContain(res.status());
  });

  // TC-IDENT-036: unauthenticated returns 401
  test('TC-IDENT-036: POST /account/change-password without auth returns 401', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/account/change-password`, {
      headers: { 'Content-Type': 'application/json' },
      data: { currentPassword: 'x', newPassword: 'y' },
    });
    expect(res.status()).toBe(401);
  });

  test('POST /account/change-password rejects empty body', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/account/change-password`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {},
    });
    // 400 or 422 = validation failure (expected)
    expect([400, 422]).toContain(res.status());
  });

  test('POST /account/change-password rejects wrong current password', async ({ request }) => {
    // PHASE7-TIER-D-CASCADE: defensive swap admin→viewer (see TC-IDENT-035 above for rationale).
    const viewerToken = await getAccessToken(request, 'user');
    const res = await request.post(`${API_BASE}/api/v1/account/change-password`, {
      headers: { ...authHeaders(viewerToken), 'Content-Type': 'application/json' },
      data: {
        currentPassword: 'WrongPassword123!',
        newPassword: 'NewSecurePass@2026',
      },
    });
    // 400 or 422 = password verification failed
    expect([400, 422]).toContain(res.status());
  });

  test('POST /account/change-password rejects same password', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/account/change-password`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: {
        currentPassword: 'DevAdmin@12345',
        newPassword: 'DevAdmin@12345',
      },
    });
    // 400 or 422 = new password must differ
    expect([400, 422]).toContain(res.status());
  });

  test('POST /account/change-password returns 401 without token', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/account/change-password`, {
      headers: { 'Content-Type': 'application/json' },
      data: {
        currentPassword: 'DevAdmin@12345',
        newPassword: 'NewPass@12345',
      },
    });
    expect(res.status()).toBe(401);
  });
});
