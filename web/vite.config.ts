import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import path from 'node:path';
import { viteStaticCopy } from 'vite-plugin-static-copy';

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [
    react(),
    // Bundle visualizer — run with: ANALYZE=true npm run build
    ...(process.env.ANALYZE === 'true'
      ? [
          (await import('rollup-plugin-visualizer')).visualizer({
            filename: 'dist/bundle-stats.html',
            gzipSize: true,
            brotliSize: true,
          }),
        ]
      : []),
    // W1: Copy VGCA SignService plugin to public/vendor/vgca/ so it is served at
    //   /vendor/vgca/vgcaplugin.js in both dev (vite dev) and prod (vite build).
    // The adapter loads it lazily at sign-time via a <script> tag injection.
    // Note: add web/public/vendor/vgca/ to .gitignore (binary copy, not source).
    viteStaticCopy({
      targets: [
        {
          // POSIX-normalize for fast-glob (vite-plugin-static-copy uses globby internally;
          // Windows backslash paths fail to match — must be forward slashes)
          src: path.resolve(__dirname, '../other/vgca_usb_token/js/vgcaplugin.js').replace(/\\/g, '/'),
          dest: 'vendor/vgca',
        },
        {
          // Bundle the pdf.js worker locally so it is served same-origin at /vendor/pdfjs/ —
          // cross-origin workers are blocked by some intranet CSP policies. signed-pdf-viewer.tsx
          // points pdfjs.GlobalWorkerOptions.workerSrc at that fixed path. pdfjs-dist is a DIRECT
          // dependency (pinned to react-pdf's version) so pnpm symlinks it top-level and this src
          // resolves — as a transitive-only dep it would not, and the copy would silently produce nothing.
          src: path.resolve(__dirname, 'node_modules/pdfjs-dist/build/pdf.worker.min.mjs').replace(/\\/g, '/'),
          dest: 'vendor/pdfjs',
        },
      ],
    }),
  ],

  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },

  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5001',
        changeOrigin: true,
      },
      '/hubs': {
        target: 'http://localhost:5001',
        changeOrigin: true,
        ws: true,
      },
      '/openapi': {
        target: 'http://localhost:5001',
        changeOrigin: true,
      },
      '/health': {
        target: 'http://localhost:5001',
        changeOrigin: true,
      },
    },
  },

  build: {
    target: 'ES2022',
    sourcemap: false,
    // Vendor chunks (antd ~1.4MB, echarts ~1.1MB) intentionally large for long-term caching;
    // raise default 500 kB hint to cover them without silencing real bundle-size regressions
    chunkSizeWarningLimit: 1500,
    rollupOptions: {
      output: {
        // Split vendor chunks for better caching
        manualChunks: {
          antd: ['antd', '@ant-design/icons'],
          echarts: ['echarts', 'echarts-for-react'],
          router: ['@tanstack/react-router'],
          query: ['@tanstack/react-query'],
          vendor: ['axios', 'react-hook-form', 'dayjs'],
          // Keep api-client + barrel in same chunk to eliminate circular-chunk warning
          // (barrel re-exports api-client; auth-provider chain loops back through barrel)
          'api-core': [
            path.resolve(__dirname, './src/core/api/api-client.ts').replace(/\\/g, '/'),
            path.resolve(__dirname, './src/core/api/index.ts').replace(/\\/g, '/'),
          ],
        },
      },
    },
  },
});
