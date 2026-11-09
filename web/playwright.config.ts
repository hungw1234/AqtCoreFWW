import { defineConfig, devices } from '@playwright/test';

// Playwright E2E configuration — targets the local Vite dev server on port 5173.
// Run with: npm run test:e2e (requires the app to be running separately).
export default defineConfig({
  testDir: './e2e',
  // Pre-warm ROPC tokens once before any worker starts — eliminates parallel token storm.
  // See: e2e/global-setup.ts
  globalSetup: './e2e/global-setup',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 1,
  workers: process.env.CI ? 1 : 3,

  reporter: [['html', { open: 'never' }]],

  use: {
    baseURL: 'http://localhost:5173',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
  },

  projects: [
    {
      name: 'compliance-qd742',
      testMatch: /qd742-compliance/,
      use: { ...devices['Desktop Chrome'] },
    },
    {
      name: 'compliance-pdpl',
      testMatch: /pdpl-compliance/,
      dependencies: ['compliance-qd742'], // runs AFTER QĐ742 (lockout test locks admin)
      use: { ...devices['Desktop Chrome'] },
    },
    {
      name: 'browser-ui',
      testMatch: /browser-ui\.spec/,
      use: { ...devices['Desktop Chrome'] },
    },
    {
      name: 'signalr',
      testMatch: /signalr/,
      use: { ...devices['Desktop Chrome'] },
    },
    {
      name: 'admin-browser',
      testMatch: /browser-ui\//,
      use: { ...devices['Desktop Chrome'] },
    },
    {
      name: 'e2e-perf',
      testMatch: /performance-baselines/,
      use: { ...devices['Desktop Chrome'] },
    },
    {
      name: 'chromium',
      testIgnore: /compliance|browser-ui|signalr|performance-baselines|accessibility/, // API-only tests run in parallel
      use: { ...devices['Desktop Chrome'] },
    },
  ],

  // Uncomment to start dev server automatically during E2E runs:
  // webServer: {
  //   command: 'npm run dev',
  //   url: 'http://localhost:5173',
  //   reuseExistingServer: !process.env.CI,
  // },
});
