/**
 * pades-orchestrator-steps.test.ts — unit tests for the per-step status machine of the PAdES orchestrator card.
 *
 * Pins the wait → process → finish | error transitions across the four phases (upload / scan / sign+OTP / download)
 * so the demo card shows which step is actively running vs done vs failed. A regression here would silently revert
 * the indicator to a coarse "all-prior-ticked" display, which is exactly what this slice set out to fix.
 */

import { describe, it, expect } from 'vitest';
import {
  buildPadesSteps,
  type PadesStepInputs,
  type Translate,
} from '@/features/digital-signature/pades-orchestrator-steps';

// Translate stub: return the fallback so descriptions are asserted by their human text.
const t: Translate = (_key, fallback) => fallback;

const BASE: PadesStepInputs = {
  uploading: false,
  fileId: null,
  scanStatus: undefined,
  padesLoading: false,
  requestId: undefined,
  signStatus: undefined,
  signedReady: false,
  hasError: false,
};

/** Extract the four step statuses in order for terse assertions. */
function statuses(input: Partial<PadesStepInputs>) {
  return buildPadesSteps({ ...BASE, ...input }, t).map((s) => s.status);
}

describe('buildPadesSteps', () => {
  it('initial idle state → all wait', () => {
    expect(statuses({})).toEqual(['wait', 'wait', 'wait', 'wait']);
  });

  it('uploading → step 0 process, rest wait', () => {
    expect(statuses({ uploading: true })).toEqual(['process', 'wait', 'wait', 'wait']);
  });

  it('uploaded + scanning → upload finish, scan process', () => {
    expect(statuses({ fileId: 'f1', scanStatus: 'Scanning' })).toEqual([
      'finish',
      'process',
      'wait',
      'wait',
    ]);
  });

  it('scan rejected → scan error, sign/done stay wait', () => {
    expect(statuses({ fileId: 'f1', scanStatus: 'Rejected', hasError: true })).toEqual([
      'finish',
      'error',
      'wait',
      'wait',
    ]);
  });

  it('scan passed, sign in flight (sync block: no requestId yet) → sign process', () => {
    // Sync flow blocks createSigningRequest before requestId exists — padesLoading must still drive the process state.
    expect(statuses({ fileId: 'f1', scanStatus: 'Available', padesLoading: true })).toEqual([
      'finish',
      'finish',
      'process',
      'wait',
    ]);
  });

  it('polling InProgress → sign process', () => {
    expect(
      statuses({ fileId: 'f1', scanStatus: 'Available', requestId: 'r1', signStatus: 'InProgress' }),
    ).toEqual(['finish', 'finish', 'process', 'wait']);
  });

  it('sign Failed → sign error', () => {
    expect(
      statuses({ fileId: 'f1', scanStatus: 'Available', requestId: 'r1', signStatus: 'Failed', hasError: true }),
    ).toEqual(['finish', 'finish', 'error', 'wait']);
  });

  it('mid-run sign error WITHOUT a terminal signStatus → sign error (not a forever spinner)', () => {
    // e.g. createSigningRequest throws after scan passed: error is set but signStatus never advances.
    expect(statuses({ fileId: 'f1', scanStatus: 'Available', hasError: true })).toEqual([
      'finish',
      'finish',
      'error',
      'wait',
    ]);
  });

  it('signed, fetching the PDF → done process', () => {
    expect(
      statuses({ fileId: 'f1', scanStatus: 'Available', requestId: 'r1', signStatus: 'Completed' }),
    ).toEqual(['finish', 'finish', 'finish', 'process']);
  });

  it('signed + blob ready → all finish', () => {
    expect(
      statuses({
        fileId: 'f1',
        scanStatus: 'Available',
        requestId: 'r1',
        signStatus: 'Completed',
        signedReady: true,
      }),
    ).toEqual(['finish', 'finish', 'finish', 'finish']);
  });

  it('download fails after Completed → done error', () => {
    expect(
      statuses({
        fileId: 'f1',
        scanStatus: 'Available',
        requestId: 'r1',
        signStatus: 'Completed',
        signedReady: false,
        hasError: true,
      }),
    ).toEqual(['finish', 'finish', 'finish', 'error']);
  });

  it('the active step carries a spinner icon; non-active steps do not', () => {
    const items = buildPadesSteps({ ...BASE, uploading: true }, t);
    expect(items[0].icon).toBeTruthy(); // process → spinner
    expect(items[1].icon).toBeUndefined(); // wait → default glyph
  });

  it('signLabels override replaces ONLY the sign step (step 2) wording — USB card reuses the same status machine', () => {
    const labels = { title: 'Ký bằng USB token', process: 'Nhập PIN…', finish: 'Đã ký', error: 'Ký thất bại' };
    const items = buildPadesSteps(
      { ...BASE, fileId: 'f1', scanStatus: 'Available', padesLoading: true },
      t,
      labels,
    );
    // Step 2 carries the override wording…
    expect(items[2].title).toBe('Ký bằng USB token');
    expect(items[2].description).toBe('Nhập PIN…'); // process state
    // …while the surrounding steps keep their default labels (not overridden).
    expect(items[0].title).toBe('Tải lên');
    expect(items[3].title).toBe('Hoàn tất');
    // Status derivation is unchanged by the override: upload+scan finish, sign in process, done waits.
    expect(items.map((s) => s.status)).toEqual(['finish', 'finish', 'process', 'wait']);
  });

  it('omitting signLabels keeps the default OTP wording (remote card unchanged)', () => {
    const items = buildPadesSteps({ ...BASE, fileId: 'f1', scanStatus: 'Available', padesLoading: true }, t);
    expect(items[2].title).toBe('Ký + OTP');
  });
});
