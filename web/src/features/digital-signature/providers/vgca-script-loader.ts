/**
 * vgca-script-loader.ts — shared, idempotent loader for the VGCA plugin SDK script.
 *
 * The VGCA SDK (/vendor/vgca/vgcaplugin.js) defines all of the window globals the
 * signing code talks to: vgca_sign_msg, vgca_get_version, vgca_sign_issued, etc.
 * Those globals do NOT exist until the script has been injected and executed.
 *
 * This module is a dependency-free leaf so that every consumer that needs the SDK
 * present can ensure it is loaded before touching a global, without creating an
 * import cycle:
 *   - the adapter sign/cert methods (vgca-usb-adapter.ts)
 *   - the availability probe (vgca-availability-probe.ts)
 *   - the doc-flow plugin invoker (vgca-docflow-methods.ts)
 *
 * Previously the loader lived privately inside vgca-usb-adapter.ts and was only
 * reachable through sign()/getCertInfo(). Code paths that hit the probe or the
 * doc-flow invoker first (e.g. the Files-page sign modal) therefore checked for a
 * global that nothing had loaded yet — always seeing it absent and reporting the
 * agent as "not installed", which gated the UI before any sign method could run
 * and trigger the load. Centralising the loader here lets those paths load-first.
 */

const VGCA_PLUGIN_SRC = '/vendor/vgca/vgcaplugin.js';

let _scriptPromise: Promise<void> | null = null;

function loadScript(src: string): Promise<void> {
  return new Promise((resolve, reject) => {
    // Early-resolve assumes any pre-existing tag with this src has finished executing.
    // Safe here because this module is the only injector and serialises via _scriptPromise,
    // so the only way to reach an existing tag is one whose load already completed.
    if (document.querySelector(`script[src="${src}"]`)) {
      resolve();
      return;
    }
    const script = document.createElement('script');
    script.src = src;
    script.onload = () => resolve();
    script.onerror = () => reject(new Error(`Failed to load VGCA plugin script: ${src}`));
    document.head.appendChild(script);
  });
}

/**
 * Ensure the VGCA SDK script is loaded (its window globals defined).
 *
 * Short-circuits when the SDK is already present (globals defined by an earlier load,
 * or by a browser-injected build). The in-flight promise is memoised so concurrent
 * callers share one network load. A failed load clears the memo so a later retry —
 * e.g. the user pressing the "retry" affordance after starting the desktop agent —
 * can re-attempt rather than being stuck on a permanently rejected promise.
 */
export async function ensureVgcaPluginLoaded(): Promise<void> {
  // vgca_sign_msg is used as the sentinel for "SDK loaded": the script defines ALL vgca_*
  // globals together, so its presence implies the others (vgca_get_version, vgca_sign_issued…)
  // are present too. Do NOT re-key this check onto a global the sentinel doesn't cover, or a
  // caller of a not-yet-defined global could short-circuit and read undefined.
  if (typeof (window as unknown as Record<string, unknown>)['vgca_sign_msg'] === 'function') return;
  if (!_scriptPromise) {
    _scriptPromise = loadScript(VGCA_PLUGIN_SRC).catch((err) => {
      _scriptPromise = null; // allow a subsequent retry to re-attempt the load
      throw err;
    });
  }
  await _scriptPromise;
}
