// Compile-time contract drift guard for generated `ApiError` schema.
// If BE removes/renames a field that `normalizeError` depends on, this file fails to
// typecheck — blocking the regeneration-without-adapter-update scenario that caused T4-04.
// Runtime OpenAPI snapshot test (Step 1 in Phase-04 plan) deferred until BE reachable at :5001.

import { describe, it, expect } from 'vitest';
import type { ApiError as GeneratedApiError } from '@/api/generated/aqtCoreFWAPIV1.schemas';
import { normalizeError } from '@/core/api/api-error';

// Fields that `normalizeError` + `groupErrorsByProperty` read from envelope errors[0].
// Keep this list in sync with api-error.ts if new fields are consumed.
type ConsumedApiErrorFields = 'code' | 'message' | 'detailVi' | 'property';

// Compile-time assertion: every consumed field must exist on generated ApiError.
// If this line errors, a BE rename/removal broke the contract — update both together.
type _AssertFieldsPresent = ConsumedApiErrorFields extends keyof GeneratedApiError ? true : never;
const _contractStillHolds: _AssertFieldsPresent = true;
void _contractStillHolds;

describe('ApiError contract drift guard', () => {
  it('generated ApiError shape is consumable by normalizeError at runtime', () => {
    // Synthesized envelope matching generated schema — if schema shifts, TS errors above.
    const envelope = {
      success: false,
      data: null,
      meta: null,
      errors: [
        { code: 'VALIDATION_001', message: 'Invalid', detailVi: 'Không hợp lệ', property: 'name', traceId: 'trace-1' },
      ] satisfies GeneratedApiError[],
    };
    const result = normalizeError(422, envelope);
    expect(result.code).toBe('VALIDATION_001');
    expect(result.message).toBe('Không hợp lệ');
    expect(result.validationErrors).toEqual({ name: ['Không hợp lệ'] });
  });
});
