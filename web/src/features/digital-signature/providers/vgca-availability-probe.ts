/**
 * vgca-availability-probe.ts — VGCA agent availability check via vgca_get_version.
 *
 * The correct probe for VGCA plugin availability is calling the exported global
 * vgca_get_version(cb) and waiting for the callback to fire with Status===0.
 * The prior WebSocket connect probe (wss://127.0.0.1:8987/GetVersion) is unreliable
 * because the plugin owns the WSS connection lifecycle — opening a parallel WS
 * can race or be refused. vgca_get_version is the canonical SDK-documented check.
 *
 * Arity (confirmed from vgcaplugin.js source):
 *   vgca_get_version(cb: (rv: string) => void): void
 * Response JSON: { Status: 0, Version: "..." } on success, { Status: N, Message: "..." } on error.
 *
 * Returns a three-state AgentProbeStatus (not a bare boolean) so the UI distinguishes
 * "plugin never installed" from "plugin present but not responding" — collapsing both
 * to false previously surfaced the wrong (install-guide) message on a transient failure.
 */

import type { AgentProbeStatus } from '../registry';
import { ensureVgcaPluginLoaded } from './vgca-script-loader';

/** Timeout for the version probe in milliseconds. */
const PROBE_TIMEOUT_MS = 5_000;

/**
 * Classify the VGCA local desktop agent state by calling vgca_get_version.
 *   - 'not-installed' : the plugin global is absent (user never installed the agent).
 *   - 'available'     : callback fired with Status===0.
 *   - 'unreachable'   : plugin present but probe timed out, threw, returned a non-zero
 *                       Status, or sent a malformed/Status-less response.
 * Never throws — every failure path resolves to a status.
 */
export async function probeVgcaAgent(): Promise<AgentProbeStatus> {
  // Inject the VGCA SDK script before probing — vgca_get_version only exists once
  // /vendor/vgca/vgcaplugin.js has loaded. The probe is frequently the first VGCA touch
  // on a page (e.g. the sign-document modal opens and probes before any sign method runs),
  // so without loading here the global is always absent and we wrongly report
  // 'not-installed', blocking the UI before the script ever has a chance to load.
  // A load failure is intentionally swallowed: the global stays absent and the genuine
  // "SDK unavailable" outcome is classified as 'not-installed' below.
  try {
    await ensureVgcaPluginLoaded();
  } catch {
    // fall through — global check below resolves to 'not-installed'
  }

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const vgcaGetVersion = (window as any)['vgca_get_version'] as
    | ((cb: (rv: string) => void) => void)
    | undefined;

  // Plugin global still absent after a load attempt → the SDK could not be loaded.
  if (typeof vgcaGetVersion !== 'function') return 'not-installed';

  // Plugin present from here on: any failure to get a healthy reply is 'unreachable',
  // never 'not-installed' (the install guide would be misleading once the plugin exists).
  return new Promise<AgentProbeStatus>((resolve) => {
    let settled = false;

    const timer = setTimeout(() => {
      if (!settled) {
        settled = true;
        resolve('unreachable');
      }
    }, PROBE_TIMEOUT_MS);

    try {
      vgcaGetVersion((rv: string) => {
        if (settled) return;
        settled = true;
        clearTimeout(timer);

        try {
          const parsed = JSON.parse(rv) as Record<string, unknown>;
          resolve(parsed['Status'] === 0 ? 'available' : 'unreachable');
        } catch {
          resolve('unreachable');
        }
      });
    } catch {
      if (!settled) {
        settled = true;
        clearTimeout(timer);
        resolve('unreachable');
      }
    }
  });
}
