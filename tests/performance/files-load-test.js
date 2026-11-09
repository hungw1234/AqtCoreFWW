// AqtCoreFW — Files module load test
// Purpose: validate upload/download SLOs (ClamAV scan is async — not measured here)
// SLO: upload p95 < 2000ms | download p95 < 500ms | error rate < 1%
// Run: k6 run files-load-test.js \
//   --env BASE_URL=https://staging.internal \
//   --env API_KEY=<key> \
//   --env TEST_TENANT_ID=00000000-0000-0000-0000-000000000001 \
//   --env TEST_USER_ID=00000000-0000-0000-0000-000000000002

import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { Rate } from 'k6/metrics';
import { getApiKeyHeaders } from './lib/auth.js';

// ─── Config ──────────────────────────────────────────────────────────────────

const BASE_URL    = __ENV.BASE_URL        || 'https://staging.internal';
const TENANT_ID   = __ENV.TEST_TENANT_ID  || '00000000-0000-0000-0000-000000000001';
const UPLOADER    = __ENV.TEST_USER_ID    || '00000000-0000-0000-0000-000000000002';
// Pre-seeded file ID for download-only runs (avoids vacuous threshold when upload returns no id)
// Set via: --env SEED_FILE_ID=<uuid-of-available-file-in-staging>
const SEED_FILE_ID = __ENV.SEED_FILE_ID || null;

// Load fixture once — shared across VUs (read-only binary)
const TEST_FILE = open('./fixtures/test-doc.pdf', 'b');

export const options = {
  stages: [
    { duration: '2m',  target: 50 },   // fewer VUs — file I/O heavier than JSON
    { duration: '10m', target: 50 },
    { duration: '2m',  target: 0  },
  ],
  thresholds: {
    'http_req_duration{operation:upload}':   ['p(95)<2000'],
    'http_req_duration{operation:download}': ['p(95)<500'],
    'http_req_failed': ['rate<0.01'],
  },
};

// ─── Custom metrics ───────────────────────────────────────────────────────────

const uploadErrors   = new Rate('files_upload_errors');
const downloadErrors = new Rate('files_download_errors');

// ─── Main scenario ────────────────────────────────────────────────────────────

export default function () {
  let fileId = null;

  // ── Upload ──────────────────────────────────────────────────────────────────
  group('Upload file', () => {
    // FilesController takes IFormFile + query params (verified)
    const uploadUrl = `${BASE_URL}/api/v1/files` +
      `?tenantId=${TENANT_ID}&uploadedBy=${UPLOADER}`;

    const res = http.post(uploadUrl,
      { file: http.file(TEST_FILE, 'test-doc.pdf', 'application/pdf') },
      { headers: getApiKeyHeaders(), tags: { operation: 'upload' } }
    );

    // 202 Accepted — async two-phase: quarantine → ClamAV scan (verified)
    const ok = check(res, {
      'POST /files status 202': (r) => r.status === 202,
    });

    uploadErrors.add(!ok);

    if (ok) {
      try { fileId = JSON.parse(res.body).data?.id; } catch { /* ignore */ }
    }
  });

  sleep(0.5);

  // ── Download — use upload id or pre-seeded fallback ──────────────────────────
  const downloadId = fileId || SEED_FILE_ID;
  if (downloadId) {
    group('Download file', () => {
      const res = http.get(`${BASE_URL}/api/v1/files/${downloadId}`, {
        headers: getApiKeyHeaders(),
        tags: { operation: 'download' },
      });

      // 200 stream or 422 if file still in quarantine (scan not yet complete)
      const ok = check(res, {
        'GET /files/:id status 200 or 422': (r) => r.status === 200 || r.status === 422,
      });

      downloadErrors.add(!ok);
    });
  }

  sleep(0.5);
}
