/**
 * registry.ts — pluggable adapter map for digital-signature providers.
 * Adapters self-register via registerAdapter() at module init time.
 * Components call resolveAdapter(providerKey) to sign/verify via the active provider.
 */

import type {
  CertPreview,
  DocFlowAdapterResult,
  DocFlowMetadata,
  SignJsonOpts,
  SignOpts,
  SignResult,
  SignXmlOpts,
} from './signature-types';

// ─── Adapter contract ─────────────────────────────────────────────────────────

/**
 * Outcome of a local-agent probe. Three-state instead of a bare boolean so the UI can
 * tell "plugin never installed" (→ install guide) apart from "plugin present but not
 * responding / token not ready" (→ retry). A plain false collapsed both into the same
 * misleading install-guide message.
 *   - 'available'     : agent responded healthy, ready to sign
 *   - 'not-installed' : plugin global absent → user must install the desktop agent
 *   - 'unreachable'   : plugin present but probe timed out / errored / returned non-OK
 */
export type AgentProbeStatus = 'available' | 'not-installed' | 'unreachable';

export interface SignatureAdapter {
  /**
   * Sign the given binary payload. Returns base64 signature + provider metadata.
   * Throws on failure — callers must handle rejection.
   */
  sign: (payload: Uint8Array, opts: SignOpts) => Promise<SignResult>;

  /**
   * Optional: probe the local agent (e.g. VGCA USB service) and classify its state.
   * Relevant for RequiresLocalAgent capability providers only.
   * Resolves a status (never throws) so callers can route the message by cause.
   */
  probeAgentStatus?: () => Promise<AgentProbeStatus>;

  /**
   * Optional (W1): fetch the end-user's signing certificate info before initiating sign.
   * Used for pre-sign certificate preview in the UI.
   * Returns null if agent is unavailable, not logged in, or cert cannot be read.
   * Implemented by usb-vgca adapter only (calls vgca_get_certinfo).
   */
  getCertInfo?: () => Promise<CertPreview | null>;

  /**
   * Optional (W2 Tier-2): sign XML content via vgca_sign_xml.
   * Returns base64-encoded bytes of the signed XMLDSig document (or signature fragment).
   * Throws on failure; callers must handle rejection.
   * Implemented by usb-vgca adapter only.
   */
  signXml?: (xmlContent: string, opts?: SignXmlOpts) => Promise<string>;

  /**
   * Optional (W2 Tier-2): sign JSON content via vgca_sign_json.
   * Returns base64-encoded JWS compact serialization string.
   * Throws on failure; callers must handle rejection.
   * Implemented by usb-vgca adapter only.
   */
  signJson?: (jsonContent: string, opts?: SignJsonOpts) => Promise<string>;

  // ─── Tier-3 doc-flow methods (W3) ─────────────────────────────────────────

  /** Văn thư phát hành — vgca_sign_issued. */
  signIssued?: (docId: string, metadata: DocFlowMetadata) => Promise<DocFlowAdapterResult>;

  /** Lãnh đạo phê duyệt — vgca_sign_approved. */
  signApproved?: (docId: string, metadata: DocFlowMetadata) => Promise<DocFlowAdapterResult>;

  /** Ký công văn đến — vgca_sign_income. */
  signIncome?: (docId: string, metadata: DocFlowMetadata) => Promise<DocFlowAdapterResult>;

  /** Góp ý / phê bình — vgca_comment. */
  comment?: (docId: string, metadata: DocFlowMetadata) => Promise<DocFlowAdapterResult>;

  /** Phụ lục đính kèm — vgca_sign_appendix. */
  signAppendix?: (docId: string, metadata: DocFlowMetadata) => Promise<DocFlowAdapterResult>;

  /** Bản sao điện tử — vgca_sign_copy. */
  signCopy?: (docId: string, metadata: DocFlowMetadata) => Promise<DocFlowAdapterResult>;

  /** Ký hàng loạt (batch) — vgca_sign_files. */
  signFiles?: (docIds: string[], metadata: DocFlowMetadata) => Promise<DocFlowAdapterResult>;
}

// ─── Internal registry ────────────────────────────────────────────────────────

const _adapters = new Map<string, SignatureAdapter>();

// ─── Public API ───────────────────────────────────────────────────────────────

/** Register an adapter for the given provider key. Overwrites prior registration. */
export function registerAdapter(key: string, adapter: SignatureAdapter): void {
  _adapters.set(key, adapter);
}

/**
 * Resolve adapter for a provider key.
 * Throws if no adapter has been registered for that key.
 */
export function resolveAdapter(key: string): SignatureAdapter {
  const adapter = _adapters.get(key);
  if (!adapter) {
    throw new Error(`No adapter registered for provider key: "${key}"`);
  }
  return adapter;
}

/** Return all currently registered provider keys. */
export function getRegisteredKeys(): string[] {
  return Array.from(_adapters.keys());
}

/** Return true if an adapter is registered for the given key. */
export function hasAdapter(key: string): boolean {
  return _adapters.has(key);
}
