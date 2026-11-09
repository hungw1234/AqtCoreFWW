import { describe, it, expect } from 'vitest';
import { deriveFilePhase } from '@/features/files/file-lifecycle-steps';

// Locks the upload→scan phase mapping that drives the lifecycle stepper in both the form File-field
// widget and the Files-page uploader. Pure function — no render needed.
describe('deriveFilePhase', () => {
  it('returns null before any upload starts', () => {
    expect(deriveFilePhase(false, false, undefined)).toBeNull();
  });

  it('reports uploading while the POST is in flight', () => {
    expect(deriveFilePhase(true, false, undefined)).toBe('uploading');
  });

  it('uploading takes precedence even if a prior file was already uploaded', () => {
    expect(deriveFilePhase(true, true, 'Available')).toBe('uploading');
  });

  it('maps a Quarantined (scan-pending) file to scanning', () => {
    expect(deriveFilePhase(false, true, 'Quarantined')).toBe('scanning');
  });

  it('maps a still-loading scan poll (undefined status) to scanning', () => {
    expect(deriveFilePhase(false, true, undefined)).toBe('scanning');
  });

  it('maps an Available file to clean', () => {
    expect(deriveFilePhase(false, true, 'Available')).toBe('clean');
  });

  it('maps a Rejected (virus-found) file to rejected', () => {
    expect(deriveFilePhase(false, true, 'Rejected')).toBe('rejected');
  });

  it('maps a timed-out scan (still Quarantined) to timeout', () => {
    expect(deriveFilePhase(false, true, 'Quarantined', true)).toBe('timeout');
  });

  it('maps a timed-out scan with no status yet to timeout', () => {
    expect(deriveFilePhase(false, true, undefined, true)).toBe('timeout');
  });

  it('a late Available verdict wins over a timeout flag', () => {
    expect(deriveFilePhase(false, true, 'Available', true)).toBe('clean');
  });

  it('a late Rejected verdict wins over a timeout flag', () => {
    expect(deriveFilePhase(false, true, 'Rejected', true)).toBe('rejected');
  });

  it('timedOut defaults to false (no fallback when omitted)', () => {
    expect(deriveFilePhase(false, true, 'Quarantined')).toBe('scanning');
  });
});
