/// <reference types="vitest" />
import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import path from 'node:path';

// Cast to `any` to bridge vite 6 / vitest 4 type-union mismatch:
// the matched overload of `defineConfig` omits some `test.*` keys (e.g. poolOptions)
// even though they are valid at runtime. Type-only workaround.
const testConfig: any = {
  // Use forks pool — threads pool crashes on Node v24 (tinypool worker exit code 124)
  pool: 'forks',
  // Cap worker count to bound RAM/CPU. Each fork boots React 19 + antd + jsdom (~300-600 MB RSS);
  // default `availableParallelism()-1` on 8-CPU box → 7 forks → 2-4 GB peak. Half-CPU cap keeps
  // peak ≤ ~2.4 GB; trades ~1.3× wall clock for bounded memory.
  // Vitest 4 pool rework: maxForks renamed to maxWorkers; execArgv moved to top level
  // (the old poolOptions block was silently ignored → cap was NOT in effect).
  maxWorkers: 2,
  // Cap V8 heap per worker (parent NODE_OPTIONS does NOT propagate to forks).
  // 1 GB/worker × 2 workers = ~2 GB hard ceiling for the worker pool.
  execArgv: ['--max-old-space-size=1024'],
  fileParallelism: true,
  maxConcurrency: 3,
  // Heavy antd+jsdom renders exceed the 5s default under 2-fork CPU contention —
  // files pass in isolation but time out non-deterministically in the full suite.
  // Assertion failures still fail immediately; only waitFor/findBy gating relaxes.
  testTimeout: 20000,
  hookTimeout: 20000,
  // Deterministic file order → vite-node transform cache (per-worker) hits across runs.
  sequence: { shuffle: false },
  environment: 'jsdom',
  globals: true,
  setupFiles: ['./src/test/setup.ts'],
  // Exclude Playwright E2E specs — they require a running app and browser
  exclude: ['**/node_modules/**', '**/e2e/**', '**/*.spec.ts'],
  coverage: {
    provider: 'v8',
    reporter: ['text', 'lcov'],
    exclude: ['src/test/**', '**/*.d.ts'],
  },
};

export default defineConfig({
  // Cast to never to avoid plugin type mismatch between vitest's bundled vite and top-level vite
  plugins: [react() as never],

  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },

  test: testConfig,
});
