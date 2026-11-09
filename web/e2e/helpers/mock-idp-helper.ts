import { type Page } from '@playwright/test';

interface MockIdpConfig {
  issuerUrl: string;
  email: string;
  name: string;
  sub: string;
}

/**
 * Intercept OIDC discovery + token + userinfo endpoints via Playwright route().
 * Used to simulate an external IdP for JIT SSO testing without a real IdP server.
 *
 * NOTE: Server-side JWT signature validation will reject unsigned tokens.
 * This helper is intended for client-side flow simulation only.
 * For full JIT SSO testing, use API-level simulation (jit-sso-flow.spec.ts).
 */
export class MockIdpHelper {
  constructor(private page: Page, private config: MockIdpConfig) {}

  async setup() {
    const { issuerUrl, email, name, sub } = this.config;

    // Intercept OIDC discovery
    await this.page.route(`${issuerUrl}/.well-known/openid-configuration`, (route) => {
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          issuer: issuerUrl,
          authorization_endpoint: `${issuerUrl}/authorize`,
          token_endpoint: `${issuerUrl}/token`,
          userinfo_endpoint: `${issuerUrl}/userinfo`,
          jwks_uri: `${issuerUrl}/.well-known/jwks`,
        }),
      });
    });

    // Intercept token exchange
    await this.page.route(`${issuerUrl}/token`, (route) => {
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          access_token: 'mock-access-token',
          id_token: this.buildMockIdToken(),
          token_type: 'Bearer',
          expires_in: 3600,
        }),
      });
    });

    // Intercept userinfo
    await this.page.route(`${issuerUrl}/userinfo`, (route) => {
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ sub, email, name }),
      });
    });
  }

  async teardown() {
    await this.page.unroute(`${this.config.issuerUrl}/**`);
  }

  private buildMockIdToken(): string {
    const header = btoa(JSON.stringify({ alg: 'none', typ: 'JWT' }));
    const payload = btoa(JSON.stringify({
      sub: this.config.sub,
      email: this.config.email,
      name: this.config.name,
      iss: this.config.issuerUrl,
      aud: 'test-client',
      exp: Math.floor(Date.now() / 1000) + 3600,
    }));
    return `${header}.${payload}.`;
  }
}
