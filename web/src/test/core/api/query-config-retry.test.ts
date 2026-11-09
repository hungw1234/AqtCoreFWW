import { describe, it, expect } from 'vitest';
import { shouldRetryQuery, queryRetryDelay } from '@/core/api/query-config';

// v2.133p16: retry guard — chặn react-query retry cho network/4xx; chỉ retry 5xx 1 lần.
// Lý do: network down + 4xx không tự khỏi sau 1s; retry mặc định nhân đôi request rác khi BE rớt.

describe('shouldRetryQuery', () => {
  it('skip retry khi không có status (network error chưa kịp parse response)', () => {
    expect(shouldRetryQuery(0, undefined)).toBe(false);
    expect(shouldRetryQuery(0, {})).toBe(false);
    expect(shouldRetryQuery(0, new Error('boom'))).toBe(false);
  });

  it('skip retry khi status=0 (ERR_CONNECTION_REFUSED / ERR_NETWORK)', () => {
    expect(shouldRetryQuery(0, { status: 0, code: '0', message: '' })).toBe(false);
  });

  it.each([400, 401, 403, 404, 409, 422, 429])('skip retry cho 4xx (%i)', (status) => {
    expect(shouldRetryQuery(0, { status, code: String(status), message: '' })).toBe(false);
  });

  it('retry 5xx 1 lần', () => {
    const err500 = { status: 500, code: '500', message: '' };
    expect(shouldRetryQuery(0, err500)).toBe(true);
    expect(shouldRetryQuery(1, err500)).toBe(false);
  });

  it.each([500, 502, 503, 504])('retry các 5xx variants (%i) lần đầu', (status) => {
    expect(shouldRetryQuery(0, { status, code: String(status), message: '' })).toBe(true);
  });
});

describe('queryRetryDelay — exponential backoff cap 8s', () => {
  it('attempt 0 → 1s', () => expect(queryRetryDelay(0)).toBe(1000));
  it('attempt 1 → 2s', () => expect(queryRetryDelay(1)).toBe(2000));
  it('attempt 2 → 4s', () => expect(queryRetryDelay(2)).toBe(4000));
  it('attempt 3 → 8s (cap)', () => expect(queryRetryDelay(3)).toBe(8000));
  it('attempt 10 → 8s (cap holds)', () => expect(queryRetryDelay(10)).toBe(8000));
});
