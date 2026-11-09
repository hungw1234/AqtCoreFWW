import { test, expect } from './fixtures/test-base';
import { AUTH_SERVER } from './helpers/auth-helper';

// OIDC Discovery + JWKS endpoint tests
// Covers: TC-AUTH-001, TC-AUTH-002

test.describe('AuthServer OIDC Discovery', () => {
  test('TC-AUTH-001: discovery document is accessible and valid', async ({ request }) => {
    const res = await request.get(`${AUTH_SERVER}/.well-known/openid-configuration`);
    expect(res.status()).toBe(200);

    const body = await res.json();
    expect(body.issuer).toBeTruthy();
    expect(body.authorization_endpoint).toContain('/connect/authorize');
    expect(body.token_endpoint).toContain('/connect/token');
    expect(body.end_session_endpoint).toContain('/connect/logout');
    expect(body.jwks_uri).toBeTruthy();

    // Required supported types
    expect(Array.isArray(body.response_types_supported)).toBe(true);
    expect(body.response_types_supported).toContain('code');

    expect(Array.isArray(body.grant_types_supported)).toBe(true);
    expect(body.grant_types_supported).toContain('authorization_code');
    expect(body.grant_types_supported).toContain('refresh_token');

    expect(Array.isArray(body.code_challenge_methods_supported)).toBe(true);
    expect(body.code_challenge_methods_supported).toContain('S256');
  });

  test('TC-AUTH-002: JWKS endpoint returns at least one signing key', async ({ request }) => {
    // Step 1: fetch jwks_uri from discovery document
    const discovery = await request.get(`${AUTH_SERVER}/.well-known/openid-configuration`);
    expect(discovery.status()).toBe(200);
    const discoveryBody = await discovery.json();

    const jwksUri: string = discoveryBody.jwks_uri;
    expect(jwksUri).toBeTruthy();

    // Step 2: fetch JWKS endpoint
    const jwks = await request.get(jwksUri);
    expect(jwks.status()).toBe(200);

    const jwksBody = await jwks.json();
    expect(Array.isArray(jwksBody.keys)).toBe(true);
    expect(jwksBody.keys.length).toBeGreaterThan(0);

    // At least one key must be RSA and used for signing or encryption
    const signingKey = jwksBody.keys.find(
      (k: { kty: string; use?: string }) =>
        k.kty === 'RSA' && (k.use === 'sig' || k.use === 'enc' || !k.use),
    );
    expect(signingKey).toBeDefined();
  });
});
