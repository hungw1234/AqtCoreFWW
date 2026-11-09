import { describe, it, expect } from 'vitest';
import { extractApiErrorMessage, extractApiErrorCode } from '@/core/api/extract-api-error';

describe('extractApiErrorMessage', () => {
  it('returns undefined for non-object rejections', () => {
    expect(extractApiErrorMessage(undefined)).toBeUndefined();
    expect(extractApiErrorMessage(null)).toBeUndefined();
    expect(extractApiErrorMessage('boom')).toBeUndefined();
    expect(extractApiErrorMessage(42)).toBeUndefined();
  });

  it('reads NormalizedApiError.message (interceptor reject shape)', () => {
    const err = { status: 409, code: 'GOV_CONFLICT', message: 'Dữ liệu đã bị thay đổi.' };
    expect(extractApiErrorMessage(err)).toBe('Dữ liệu đã bị thay đổi.');
  });

  it('does NOT surface a bare JS Error message (no code/status)', () => {
    // Guards against leaking internal errors like "Cannot read properties of undefined".
    expect(extractApiErrorMessage(new Error('Cannot read properties of undefined'))).toBeUndefined();
  });

  it('reads raw envelope per-field detailVi first', () => {
    const err = {
      response: { data: { errors: [{ detailVi: 'URL trùng lặp.', message: 'Duplicate URL.', code: 'GOV_DUP' }] } },
    };
    expect(extractApiErrorMessage(err)).toBe('URL trùng lặp.');
  });

  it('falls back to per-field message when detailVi absent', () => {
    const err = { response: { data: { errors: [{ message: 'Duplicate URL.' }] } } };
    expect(extractApiErrorMessage(err)).toBe('Duplicate URL.');
  });

  it('falls back to envelope-level message when no per-field error', () => {
    const err = { response: { data: { message: 'Yêu cầu không hợp lệ.' } } };
    expect(extractApiErrorMessage(err)).toBe('Yêu cầu không hợp lệ.');
  });

  it('prefers the raw envelope message over a normalized message on the same object', () => {
    const err = {
      status: 422,
      code: 'GOV_X',
      message: 'normalized',
      response: { data: { errors: [{ detailVi: 'envelope' }] } },
    };
    expect(extractApiErrorMessage(err)).toBe('envelope');
  });

  it('returns undefined for an antd validateFields rejection', () => {
    // antd reject shape carries no message/code/status nor a response envelope.
    const antdReject = { errorFields: [{ name: ['url'], errors: ['Required'] }], values: {}, outOfDate: false };
    expect(extractApiErrorMessage(antdReject)).toBeUndefined();
  });

  it('ignores empty-string messages', () => {
    expect(extractApiErrorMessage({ code: 'X', message: '' })).toBeUndefined();
    expect(extractApiErrorMessage({ response: { data: { errors: [{ message: '' }] } } })).toBeUndefined();
  });
});

describe('extractApiErrorCode', () => {
  it('reads the raw envelope code first', () => {
    const err = { code: 'NORM', response: { data: { errors: [{ code: 'GOV_DUP' }] } } };
    expect(extractApiErrorCode(err)).toBe('GOV_DUP');
  });

  it('falls back to the normalized code', () => {
    expect(extractApiErrorCode({ status: 404, code: 'GOV_NOT_FOUND' })).toBe('GOV_NOT_FOUND');
  });

  it('returns undefined when no code present', () => {
    expect(extractApiErrorCode({ message: 'x' })).toBeUndefined();
    expect(extractApiErrorCode(null)).toBeUndefined();
  });
});
