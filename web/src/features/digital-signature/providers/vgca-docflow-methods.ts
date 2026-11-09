/**
 * vgca-docflow-methods.ts — VGCA Tier-3 doc-flow adapter methods (W3).
 *
 * 7 methods, 1-to-1 with VGCA plugin functions for Vietnamese government document signing.
 * All methods share the same `_invokeDocFlow` private helper which:
 *   1. Calls POST /api/signing/docflow/initiate to get {handlerUrl, jwt, docId}
 *   2. Calls the VGCA plugin function with FileUploadHandler + JWTToken params
 *   3. Resolves when the plugin callback fires with Status=0
 *
 * VGCA doc-flow plugin function contract (verified against SDK sample Demo.htm):
 *   vgca_sign_issued(paramsJson, cb)   ← 2-arg, NO sender (unlike raw vgca_sign_msg)
 *   params: { FileUploadHandler, SessionId, JWTToken, FileName, DocNumber, IssuedDate, MetaData? }
 *   callback: cb(responseJson)         ← 1-arg, the response JSON string only
 *   response success: { Status: 0, FileServer: <storage url>, FileName, DocumentNumber, DocumentDate }
 *   response failure: { Status: <nonzero>, Message: <msg> }
 *
 * All 7 functions (Issued, Approved, Income, Comment, Appendix, Copy, Files) share the
 * same params shape — they differ only in plugin function name and sign-type routing.
 */

import { initiateDocFlow } from '../signature-api';
import { ensureVgcaPluginLoaded } from './vgca-script-loader';
import type { DocFlowAdapterResult, DocFlowMetadata, DocFlowSignType, DocFlowSignTypeName } from '../signature-types';

const DOCFLOW_TIMEOUT_MS = 120_000; // 2 min — VGCA PIN dialog + signing can be slow

// ─── Sign-type → plugin function name map ────────────────────────────────────

/**
 * Maps a DocFlowSignTypeName to the VGCA window global function name.
 * Used by callers that already have a handoff from the unified signing endpoint
 * and need to know which plugin fn to invoke without calling /initiate again.
 */
export const SIGN_TYPE_TO_PLUGIN_FN: Record<DocFlowSignTypeName, string> = {
  Issued:   'vgca_sign_issued',
  Approved: 'vgca_sign_approved',
  Income:   'vgca_sign_income',
  Comment:  'vgca_comment',
  Appendix: 'vgca_sign_appendix',
  Copy:     'vgca_sign_copy',
  Files:    'vgca_sign_files',
};

/** Returns the VGCA plugin function name for a given sign type. */
export function signTypeToPluginFn(signType: DocFlowSignTypeName): string {
  return SIGN_TYPE_TO_PLUGIN_FN[signType];
}

// ─── Exported plugin invocation core ─────────────────────────────────────────

/**
 * Invoke the VGCA plugin function directly using a pre-obtained handoff.
 * Used by the unified signing path (createSigningRequest already returned the handoff)
 * to avoid a second /initiate call which would create a duplicate signing record.
 *
 * @param pluginFnName - VGCA global function name, e.g. "vgca_sign_issued"
 * @param handoff      - {handlerUrl, jwt, docId} from createSigningRequest clientHandoff
 * @param metadata     - document metadata for the VGCA stamp (docNumber, issuedDate, fileName)
 */
export async function runVgcaPlugin(
  pluginFnName: string,
  handoff: { handlerUrl: string; jwt: string; docId: string },
  metadata: Pick<DocFlowMetadata, 'docNumber' | 'issuedDate' | 'fileName' | 'extra'>,
): Promise<DocFlowAdapterResult> {
  // Ensure the VGCA SDK script is loaded so the plugin global exists. Callers may reach
  // here without any prior sign() call having injected the script (the unified signing
  // path goes create-request → runVgcaPlugin directly), so the global would otherwise be
  // absent and every ceremony would fail with "not available" even with the agent running.
  await ensureVgcaPluginLoaded();

  // VGCA DocFlow plugin functions take TWO args — (paramsJson, callback) — with NO leading
  // `sender`, and the callback receives a SINGLE response-JSON string. Verified against the
  // VGCA SDK sample (other/vgca_usb_token/sample/Demo.htm): `vgca_sign_issued(json_prms, cb)`
  // and `function cb(rv){ JSON.parse(rv) }`. This differs from the raw `vgca_sign_msg` path
  // (vgca-usb-adapter.ts), which IS 3-arg with a sender — do NOT unify the two signatures.
  // Passing a spurious `sender` here shifts the params string into the callback slot, so the
  // plugin invokes the JSON string as a function on the WSS reply ("X is not a function") and
  // our callback never fires — the ceremony hangs until timeout.
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const vgcaFn = (window as any)[pluginFnName] as
    | ((params: string, cb: (response: string) => void) => void)
    | undefined;

  if (typeof vgcaFn !== 'function') {
    throw new Error(
      `${pluginFnName} not available — VGCA plugin not loaded or VGCASignService not running`,
    );
  }

  // FileName is the URL the desktop tool DOWNLOADS the to-be-signed document from — the VGCA
  // integration doc defines FileName as "đường dẫn văn bản ký số" (the document's path/URL), NOT a
  // display name, and the tool passes it straight to WebRequest.Create. It MUST therefore be an
  // absolute URL: a bare filename is a relative URI and throws "Invalid URI: the format of the URI
  // could not be determined" inside the tool, surfacing as "Tải tệp ký số không thành công (0x0023)".
  // Point it at the gated handler download carrying the docId + JWT the [AllowAnonymous] endpoint
  // requires (the tool has no separate docId field of its own to send). Upload still POSTs to
  // FileUploadHandler; the JWT nonce allows two uses (download slot 1, upload slot 2).
  const downloadUrl =
    `${handoff.handlerUrl}?docId=${encodeURIComponent(handoff.docId)}` +
    `&token=${encodeURIComponent(handoff.jwt)}`;

  const params = JSON.stringify({
    FileUploadHandler: handoff.handlerUrl,
    SessionId:         '', // empty → authenticate via JWTToken (matches SDK sample)
    JWTToken:          handoff.jwt,
    FileName:          downloadUrl,
    DocNumber:         metadata.docNumber,
    IssuedDate:        metadata.issuedDate.toISOString().slice(0, 10), // YYYY-MM-DD
    // MetaData (array of {Key,Value}) is only sent when the caller supplies stamp metadata;
    // the sample omits it for plain issued/approved signing.
    ...(metadata.extra ? { MetaData: metadata.extra } : {}),
  });

  return new Promise<DocFlowAdapterResult>((resolve, reject) => {
    const timer = setTimeout(() => {
      reject(
        new Error(
          `${pluginFnName} timed out after ${DOCFLOW_TIMEOUT_MS / 1000}s — ` +
          'check that VGCASignService is running and the user completed the PIN dialog',
        ),
      );
    }, DOCFLOW_TIMEOUT_MS);

    vgcaFn(params, (response: string) => {
      clearTimeout(timer);
      try {
        const parsed = JSON.parse(response) as Record<string, unknown>;
        const status    = parsed['Status'];
        const fileServer = parsed['FileServer'];
        const message   = parsed['Message'] ?? parsed['Error'] ?? response;

        // Coerce before comparing: the SDK sample checks Status loosely (`== 0`), so the agent
        // may return Status as the number 0 or the string "0". A strict `!== 0` would report a
        // successful sign as a failure if the agent sends a string. Number("0")===0; a missing
        // or non-numeric status coerces to NaN and is correctly treated as failure.
        if (Number(status) !== 0) {
          reject(new Error(`${pluginFnName} failed (Status=${status}): ${message}`));
          return;
        }

        resolve({
          status:     'Completed',
          storageKey: typeof fileServer === 'string' ? fileServer : null,
          fileServer: typeof fileServer === 'string' ? fileServer : undefined,
        });
      } catch {
        reject(new Error(`${pluginFnName} response parse error: ${response}`));
      }
    });
  });
}

// ─── Private helper ───────────────────────────────────────────────────────────

/**
 * Core doc-flow invocation helper shared by all 7 sign-type wrappers.
 * Calls /initiate first (legacy path), then invokes the VGCA plugin.
 * Callers that already have a handoff from the unified signing endpoint
 * should use runVgcaPlugin directly to avoid creating a duplicate record.
 *
 * @param pluginFnName - VGCA global function name, e.g. "vgca_sign_issued"
 * @param docId        - Document UUID (will be passed to /initiate)
 * @param signType     - Signing type enum value for /initiate request
 * @param metadata     - DocFlowMetadata (DocNumber, IssuedDate, FileName, OriginalStorageKey)
 */
async function _invokeDocFlow(
  pluginFnName: string,
  docId: string,
  signType: DocFlowSignType,
  metadata: DocFlowMetadata,
): Promise<DocFlowAdapterResult> {
  // Step 1: initiate doc-flow session — BE creates record + issues JWT
  const initiated = await initiateDocFlow(docId, signType, metadata);

  // Step 2 + 3: invoke plugin using the handoff from /initiate
  return runVgcaPlugin(pluginFnName, initiated, metadata);
}

// ─── Public doc-flow methods ──────────────────────────────────────────────────

/** Văn thư phát hành — vgca_sign_issued */
export async function signIssued(
  docId: string,
  metadata: DocFlowMetadata,
): Promise<DocFlowAdapterResult> {
  return _invokeDocFlow('vgca_sign_issued', docId, 'Issued', metadata);
}

/** Lãnh đạo phê duyệt — vgca_sign_approved */
export async function signApproved(
  docId: string,
  metadata: DocFlowMetadata,
): Promise<DocFlowAdapterResult> {
  return _invokeDocFlow('vgca_sign_approved', docId, 'Approved', metadata);
}

/** Ký công văn đến — vgca_sign_income */
export async function signIncome(
  docId: string,
  metadata: DocFlowMetadata,
): Promise<DocFlowAdapterResult> {
  return _invokeDocFlow('vgca_sign_income', docId, 'Income', metadata);
}

/** Góp ý / phê bình — vgca_comment */
export async function comment(
  docId: string,
  metadata: DocFlowMetadata,
): Promise<DocFlowAdapterResult> {
  return _invokeDocFlow('vgca_comment', docId, 'Comment', metadata);
}

/** Phụ lục đính kèm — vgca_sign_appendix */
export async function signAppendix(
  docId: string,
  metadata: DocFlowMetadata,
): Promise<DocFlowAdapterResult> {
  return _invokeDocFlow('vgca_sign_appendix', docId, 'Appendix', metadata);
}

/** Bản sao điện tử — vgca_sign_copy */
export async function signCopy(
  docId: string,
  metadata: DocFlowMetadata,
): Promise<DocFlowAdapterResult> {
  return _invokeDocFlow('vgca_sign_copy', docId, 'Copy', metadata);
}

/** Ký hàng loạt — vgca_sign_files (batch: first docId drives the session) */
export async function signFiles(
  docIds: string[],
  metadata: DocFlowMetadata,
): Promise<DocFlowAdapterResult> {
  if (docIds.length === 0) throw new Error('docIds must not be empty for batch signing.');
  return _invokeDocFlow('vgca_sign_files', docIds[0], 'Files', metadata);
}
