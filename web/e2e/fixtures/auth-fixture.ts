import { type Page, type APIRequestContext } from '@playwright/test';
import { test as base } from './test-base';
import { LoginPage } from '../pages/login-page';
import { TestDataFactory } from './test-data-factory';
import { TestDataModulesFactory } from './test-data-modules-factory';
import { getAccessToken } from '../helpers/auth-helper';

/**
 * Extended Playwright test fixture providing:
 * - authedPage: browser page with OIDC login completed
 * - apiToken: ROPC access token for API calls (admin)
 * - userToken: ROPC token for unprivileged user (viewer@dev.local)
 * - officerToken: ROPC token for GovOfficer role
 * - testData: factory for Identity entities (users, groups, etc.)
 * - moduleData: factory for module entities (files, notifications, etc.)
 *
 * All fixtures are worker-scoped (shared across tests in a file).
 */
type AuthFixtures = {
  authedPage: Page;
  apiToken: string;
  userToken: string;
  officerToken: string;
  apiContext: APIRequestContext;
  testData: TestDataFactory;
  moduleData: TestDataModulesFactory;
};

// eslint-disable-next-line @typescript-eslint/no-empty-object-type -- Playwright idiom: no test-scoped fixtures, only worker-scoped ones
export const test = base.extend<{}, AuthFixtures>({
  authedPage: [async ({ browser }, use) => {
    const ctx = await browser.newContext();
    const page = await ctx.newPage();
    const loginPage = new LoginPage(page);
    await loginPage.loginAsAdmin();
    await use(page);
    await ctx.close();
  }, { scope: 'worker', timeout: 120_000 }],

  apiToken: [async ({ playwright }, use) => {
    const ctx = await playwright.request.newContext();
    const token = await getAccessToken(ctx);
    await use(token);
    await ctx.dispose();
  }, { scope: 'worker' }],

  userToken: [async ({ playwright }, use) => {
    const ctx = await playwright.request.newContext();
    const token = await getAccessToken(ctx, 'user');
    await use(token);
    await ctx.dispose();
  }, { scope: 'worker' }],

  officerToken: [async ({ playwright }, use) => {
    const ctx = await playwright.request.newContext();
    const token = await getAccessToken(ctx, 'officer');
    await use(token);
    await ctx.dispose();
  }, { scope: 'worker' }],

  apiContext: [async ({ playwright, apiToken }, use) => {
    const ctx = await playwright.request.newContext({
      extraHTTPHeaders: { Authorization: `Bearer ${apiToken}` },
    });
    await use(ctx);
    await ctx.dispose();
  }, { scope: 'worker' }],

  testData: [async ({ playwright, apiToken }, use) => {
    const ctx = await playwright.request.newContext({
      extraHTTPHeaders: {
        Authorization: `Bearer ${apiToken}`,
        'Content-Type': 'application/json',
      },
    });
    const factory = new TestDataFactory(ctx);
    await use(factory);
    await factory.cleanup();
    await ctx.dispose();
  }, { scope: 'worker' }],

  moduleData: [async ({ playwright, apiToken }, use) => {
    const ctx = await playwright.request.newContext({
      extraHTTPHeaders: {
        Authorization: `Bearer ${apiToken}`,
        'Content-Type': 'application/json',
      },
    });
    const factory = new TestDataModulesFactory(ctx);
    await use(factory);
    await factory.cleanup();
    await ctx.dispose();
  }, { scope: 'worker' }],
});

export { expect } from './test-base';
