// orval generates TanStack Query hooks from the backend OpenAPI spec.
//
// Usage:
//   1. Start Docker: `docker compose up -d` in infra/docker/
//   2. Run: npm run generate:api
//   3. Generated hooks appear in src/api/generated/ (one folder per controller tag)
//   4. Import hooks directly: import { useGetAuditLogs } from '@/api/generated/audit'
//
// orval docs: https://orval.dev
// TanStack Query v5 output: https://orval.dev/reference/configuration/output#client

import { defineConfig } from 'orval';

export default defineConfig({
  api: {
    input: {
      // OpenAPI spec via nginx proxy (port 5001)
      target: 'http://localhost:5001/openapi/v1.json',
    },
    output: {
      target: 'src/api/generated',
      client: 'react-query',
      // orval 8 defaults to fetch; the mutator below is axios-based.
      httpClient: 'axios',
      mode: 'tags-split', // one file per OpenAPI tag/controller
      // orval 8 adds a barrel index.ts by default; consumers import per-tag files, so keep v7's layout.
      indexFiles: false,
      override: {
        mutator: {
          path: './src/core/api/api-client.ts',
          name: 'apiClientMutator',
        },
      },
    },
  },
});
