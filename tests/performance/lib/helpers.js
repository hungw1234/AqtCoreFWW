// k6 shared helper utilities
// Usage: import { randomRef, pageParams, parseBody } from './lib/helpers.js';

/** Generate unique reference string for load test resources */
export function randomRef(prefix = 'LOAD') {
  return `${prefix}-${Date.now()}-${Math.floor(Math.random() * 100000)}`;
}

/** Build pagination query string */
export function pageParams(page = 1, pageSize = 20) {
  return `page=${page}&pageSize=${pageSize}`;
}

/** Parse JSON response body safely — returns null on error */
export function parseBody(res) {
  try { return JSON.parse(res.body); } catch { return null; }
}

/** Get test tenant ID from env or default */
export function getTenantId() {
  return __ENV.TEST_TENANT_ID || '00000000-0000-0000-0000-000000000001';
}

/** Build a valid CreateCaseCommand payload matching API contract */
export function casePayload(suffix = '') {
  return JSON.stringify({
    tenantId:    getTenantId(),
    title:       `Load test case ${randomRef()}${suffix}`,
    description: 'Created by k6 load test — automated performance validation for SLO verification',
    type:        0, // Application
    priority:    1, // Medium
  });
}
