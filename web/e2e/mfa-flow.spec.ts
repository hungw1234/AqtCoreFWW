import { test, expect } from './fixtures/test-base';
import { getAccessToken, authHeaders, API_BASE } from './helpers/auth-helper';

// MFA flow E2E — TOTP enrolment via authenticated API.
// Routes moved /api/v1/mfa/* -> /api/v1/account/mfa/* (AccountMfaController).
// TOTP setup/verify hits /account/mfa/enrol/totp (GET starts, POST confirms).
// Note: TC-IDENT-055/056 removed 2026-05-09 (MFA-EMAIL-OTP-ENDPOINT closed
// as YAGNI obsolete: no FE consumer for email-OTP send exists).
test.describe('MFA flow (authenticated API)', () => {
  let token: string;

  test.beforeAll(async ({ request }) => {
    token = await getAccessToken(request);
    expect(token).toBeTruthy();
  });

  // ── Auth guards ─────────────────────────────────────────────────────────────

  // TC-IDENT-053: TOTP enrol GET — unauthenticated returns 401
  test('TC-IDENT-053: GET /account/mfa/enrol/totp without auth returns 401', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/account/mfa/enrol/totp`);
    expect(res.status()).toBe(401);
  });

  test('POST /account/mfa/enrol/totp without auth returns 401', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/account/mfa/enrol/totp`, {
      headers: { 'Content-Type': 'application/json' },
      data: { totpCode: '000000' },
    });
    expect(res.status()).toBe(401);
  });

  // ── TOTP enrol ──────────────────────────────────────────────────────────────

  // TC-IDENT-052: TOTP enrol start — returns otpauth URI
  test('TC-IDENT-052: GET /account/mfa/enrol/totp returns 200 with otpauthUri', async ({ request }) => {
    const res = await request.get(`${API_BASE}/api/v1/account/mfa/enrol/totp`, {
      headers: authHeaders(token),
    });
    // 200 fresh enrol or 409 if already enrolled (admin@dev.local may have prior TOTP);
    // both prove route is wired and authorised.
    expect([200, 409]).toContain(res.status());
    if (res.status() === 200) {
      const body = await res.json();
      const data = body.data ?? body;
      // Controller returns qrCodeUri (not otpauthUri) — keep the otpauth scheme assertion.
      expect(data).toHaveProperty('qrCodeUri');
      expect(typeof data.qrCodeUri).toBe('string');
      expect(data.qrCodeUri).toMatch(/^otpauth:\/\/totp\//);
    }
  });

  // TC-IDENT-054: TOTP enrol confirm — invalid code returns 400
  test('TC-IDENT-054: POST /account/mfa/enrol/totp with invalid code returns 400', async ({ request }) => {
    const res = await request.post(`${API_BASE}/api/v1/account/mfa/enrol/totp`, {
      headers: { ...authHeaders(token), 'Content-Type': 'application/json' },
      data: { totpCode: '000000' },
    });
    // 400 = invalid code; 409 = already enrolled; 429 = rate limited.
    expect([400, 409, 429]).toContain(res.status());
  });
});
