import { describe, it, expect } from 'vitest';
import { normalizeError } from '@/core/api/api-error';

describe('normalizeError', () => {
  it('prefers detail_vi (Vietnamese) over English detail', () => {
    const err = normalizeError(400, {
      title: 'Bad Request',
      detail: 'Invalid input',
      detail_vi: 'Dữ liệu đầu vào không hợp lệ',
    });
    expect(err.message).toBe('Dữ liệu đầu vào không hợp lệ');
    expect(err.status).toBe(400);
  });

  it('falls back to English detail when detail_vi absent', () => {
    const err = normalizeError(404, { detail: 'Resource not found' });
    expect(err.message).toBe('Resource not found');
    expect(err.status).toBe(404);
  });

  it('uses default Vietnamese message when body is empty', () => {
    const err = normalizeError(500, null);
    expect(err.message).toBe('Lỗi hệ thống. Vui lòng liên hệ quản trị viên.');
    expect(err.status).toBe(500);
  });

  it('uses default Vietnamese message for 401', () => {
    const err = normalizeError(401, {});
    expect(err.message).toBe('Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.');
  });

  it('uses default Vietnamese message for 403', () => {
    const err = normalizeError(403, {});
    expect(err.message).toBe('Bạn không có quyền thực hiện thao tác này.');
  });

  it('extracts validation errors from RFC 9457 errors map', () => {
    const err = normalizeError(422, {
      detail_vi: 'Dữ liệu không hợp lệ',
      errors: { Name: ['Tên không được để trống'], Email: ['Email không đúng định dạng'] },
    });
    expect(err.validationErrors).toEqual({
      Name: ['Tên không được để trống'],
      Email: ['Email không đúng định dạng'],
    });
  });

  it('extracts code from backend error code field', () => {
    const err = normalizeError(400, { code: 'GOV_RPT_001', detail_vi: 'Lỗi báo cáo' });
    expect(err.code).toBe('GOV_RPT_001');
  });

  it('falls back to string status as code when no code in body', () => {
    const err = normalizeError(503, { detail: 'Service unavailable' });
    expect(err.code).toBe('503');
  });

  it('picks message from backend envelope errors[0] for 422 instead of generic fallback', () => {
    const err = normalizeError(422, {
      success: false,
      data: null,
      meta: null,
      errors: [
        {
          code: 'GOV_SYS_002',
          message: "Mã biểu mẫu phải bắt đầu bằng chữ cái hoặc '_', chỉ chứa chữ cái, số, '_', '-'.",
          detailVi: null,
          property: 'Code',
        },
      ],
    });
    expect(err.code).toBe('GOV_SYS_002');
    expect(err.message).toContain('Mã biểu mẫu phải bắt đầu');
    expect(err.validationErrors).toEqual({
      Code: ["Mã biểu mẫu phải bắt đầu bằng chữ cái hoặc '_', chỉ chứa chữ cái, số, '_', '-'."],
    });
  });

  it('prefers envelope errors[0].detailVi over message when both present', () => {
    const err = normalizeError(422, {
      success: false,
      errors: [{ code: 'GOV_X', message: 'English', detailVi: 'Tiếng Việt', property: 'F' }],
    });
    expect(err.message).toBe('Tiếng Việt');
  });
});
