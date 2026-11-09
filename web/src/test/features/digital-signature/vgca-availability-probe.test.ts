/**
 * vgca-availability-probe.test.ts — unit tests for VGCA agent availability detection.
 *
 * W7 tests for probeAgentStatus() behavior (three-state classification):
 *   - 'not-installed' when plugin not loaded (window.vgca_get_version undefined)
 *   - 'available' when vgca_get_version callback fires with Status:0
 *   - 'unreachable' when callback fires with non-zero Status (service error)
 *   - 'unreachable' on 5s timeout (probe never responds)
 *   - 'unreachable' on malformed / Status-less response (plugin present, bad reply)
 *
 * Uses vi.useFakeTimers() for deterministic timeout testing.
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { resolveAdapter } from '@/features/digital-signature/registry';

// ─── Stub document APIs before adapter loads ────────────────────────────────

const mockAppendChild = vi.fn();
vi.stubGlobal('document', {
  ...globalThis.document,
  querySelector: () => null,
  createElement: (tag: string) => {
    if (tag === 'script') {
      const el: Record<string, unknown> = { src: '' };
      Object.defineProperty(el, 'onload', {
        set(fn: () => void) {
          Promise.resolve().then(fn);
        },
        get() { return undefined; },
      });
      return el;
    }
    return globalThis.document.createElement(tag);
  },
  head: { appendChild: mockAppendChild },
});

// ─── Pre-populate vgca globals for initial load ──────────────────────────────

vi.stubGlobal('vgca_sign_msg', vi.fn());
vi.stubGlobal('vgca_get_version', vi.fn());

// ─── Import adapter after stubs in place ────────────────────────────────────

import '@/features/digital-signature/providers/vgca-usb-adapter';

describe('vgca-availability-probe — probeAgentStatus()', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  // ── Test 1: not-installed when plugin not loaded ──────────────────────────

  it("returns 'not-installed' when window.vgca_get_version is undefined", async () => {
    vi.stubGlobal('vgca_get_version', undefined);
    const adapter = resolveAdapter('usb-vgca');

    const status = await adapter.probeAgentStatus!();

    expect(status).toBe('not-installed');
  });

  // ── Test 2: available when callback fires with Status:0 ───────────────────

  it("returns 'available' when vgca_get_version callback fires with Status:0", async () => {
    vi.stubGlobal('vgca_get_version', (cb: (rv: string) => void) => {
      Promise.resolve().then(() => {
        cb(JSON.stringify({ Status: 0, Version: '1.2.3' }));
      });
    });

    const adapter = resolveAdapter('usb-vgca');
    const status = await adapter.probeAgentStatus!();

    expect(status).toBe('available');
  });

  // ── Test 3: unreachable when callback fires with Status:500 ───────────────

  it("returns 'unreachable' when vgca_get_version callback fires with Status:500 (service error)", async () => {
    vi.stubGlobal('vgca_get_version', (cb: (rv: string) => void) => {
      Promise.resolve().then(() => {
        cb(JSON.stringify({ Status: 500, Message: 'Internal server error' }));
      });
    });

    const adapter = resolveAdapter('usb-vgca');
    const status = await adapter.probeAgentStatus!();

    expect(status).toBe('unreachable');
  });

  // ── Test 4: unreachable on 5s timeout (probe never responds) ──────────────

  it("returns 'unreachable' on 5 second timeout when callback never fires", async () => {
    // Mock vgca_get_version to never call the callback
    vi.stubGlobal('vgca_get_version', (_cb: (rv: string) => void) => {
      // Intentionally never call cb
    });

    // Use fake timers for deterministic timeout testing
    vi.useFakeTimers();

    const adapter = resolveAdapter('usb-vgca');
    const statusPromise = adapter.probeAgentStatus!();

    // Advance past the 5s timeout. Use the async variant so the microtask from the
    // load-before-probe await (ensureVgcaPluginLoaded) flushes and the probe's setTimeout
    // is registered before time advances — otherwise the timer fires against nothing.
    await vi.advanceTimersByTimeAsync(6000);

    const status = await statusPromise;

    vi.useRealTimers();

    expect(status).toBe('unreachable');
  });

  // ── Test 5: unreachable when callback fires with malformed JSON ───────────

  it("returns 'unreachable' when vgca_get_version callback provides malformed JSON", async () => {
    vi.stubGlobal('vgca_get_version', (cb: (rv: string) => void) => {
      Promise.resolve().then(() => {
        cb('{ invalid json }');
      });
    });

    const adapter = resolveAdapter('usb-vgca');
    const status = await adapter.probeAgentStatus!();

    expect(status).toBe('unreachable');
  });

  // ── Test 6: unreachable when callback fires with missing Status field ──────

  it("returns 'unreachable' when callback response lacks Status field", async () => {
    vi.stubGlobal('vgca_get_version', (cb: (rv: string) => void) => {
      Promise.resolve().then(() => {
        cb(JSON.stringify({ Version: '1.2.3' })); // missing Status
      });
    });

    const adapter = resolveAdapter('usb-vgca');
    const status = await adapter.probeAgentStatus!();

    expect(status).toBe('unreachable');
  });

  // ── Test 7: probe loads the SDK script first when no global is present yet ──
  // Regression guard for the deadlock where the probe — often the first VGCA touch on a
  // page — checked window.vgca_get_version without injecting /vendor/vgca/vgcaplugin.js
  // first, always saw it absent, and reported 'not-installed', gating the UI before any
  // sign method could run and load the script. The probe must load-first, then classify.

  it("injects the SDK script before probing and returns 'available' once it defines vgca_get_version", async () => {
    // Fresh-page state: neither SDK global present yet (no sign method has run).
    vi.stubGlobal('vgca_sign_msg', undefined);
    vi.stubGlobal('vgca_get_version', undefined);

    // Simulate vgcaplugin.js defining its globals when the injected <script> loads.
    vi.spyOn(globalThis.document, 'createElement').mockImplementationOnce(((tag: string) => {
      if (tag === 'script') {
        const el: Record<string, unknown> = { src: '' };
        Object.defineProperty(el, 'onload', {
          set(fn: () => void) {
            Promise.resolve().then(() => {
              vi.stubGlobal('vgca_sign_msg', vi.fn());
              vi.stubGlobal('vgca_get_version', (cb: (rv: string) => void) => {
                Promise.resolve().then(() => cb(JSON.stringify({ Status: 0, Version: '1.0' })));
              });
              fn();
            });
          },
          get() { return undefined; },
        });
        return el;
      }
      return globalThis.document.createElement(tag);
    }) as unknown as typeof document.createElement);

    const adapter = resolveAdapter('usb-vgca');
    const status = await adapter.probeAgentStatus!();

    // Without the load-before-probe fix the probe would have seen vgca_get_version absent
    // and returned 'not-installed'. Loading first lets the genuine probe run → 'available'.
    expect(status).toBe('available');
  });
});
