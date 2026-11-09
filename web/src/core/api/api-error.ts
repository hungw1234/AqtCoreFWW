import i18next from 'i18next';
import type { ApiError as GeneratedApiError } from '@/api/generated/aqtCoreFWAPIV1.schemas';

// A single graph-validation violation surfaced via the BE default-deny opt-in PublicMetadata envelope.
// Wire path: errors[0].metadata.violations[*]. Outer keys are camelCase (anon-type props); the inner
// `metadata` dictionary keys stay PascalCase (e.g. UnknownGroupCodes) — no global DictionaryKeyPolicy on BE.
export interface GraphViolation {
  rule: string;
  stateName?: string | null;
  transitionActionName?: string | null;
  detail?: string | null;
  metadata?: Record<string, unknown> | null;
}

// Normalized API error shape used throughout the app
export interface NormalizedApiError {
  status: number;
  code: string;        // e.g. "GOV_RPT_001"
  message: string;     // Vietnamese-first if available
  detail?: string;
  validationErrors?: Record<string, string[]>;
  violations?: GraphViolation[];  // structured graph-validation violations (workflow/BP graph save/activate)
}

// Internal shape from backend RFC 9457 problem details
interface ProblemDetail {
  title?: string;
  detail?: string;
  detail_vi?: string;
  status?: number;
  errors?: Record<string, string[]>;
  code?: string;
}

/** Group backend errors[] by property → validationErrors map (for form field highlighting) */
function groupErrorsByProperty(errors: GeneratedApiError[]): Record<string, string[]> | undefined {
  const map: Record<string, string[]> = {};
  for (const e of errors) {
    const key = e.property ?? '_';
    (map[key] ??= []).push(e.detailVi ?? e.message);
  }
  return Object.keys(map).length > 0 ? map : undefined;
}

/** Pull structured graph violations from the opt-in PublicMetadata envelope (errors[0].metadata.violations[]).
 * Defensive: BE may omit metadata entirely on non-graph errors, so every hop is shape-checked. */
function extractViolations(firstError: GeneratedApiError | undefined): GraphViolation[] | undefined {
  const meta = (firstError as { metadata?: unknown } | undefined)?.metadata;
  if (!meta || typeof meta !== 'object') return undefined;
  const raw = (meta as { violations?: unknown }).violations;
  if (!Array.isArray(raw)) return undefined;
  const violations = raw
    .filter((v): v is Record<string, unknown> => v != null && typeof v === 'object')
    .map((v) => ({
      rule: String(v.rule ?? ''),
      stateName: (v.stateName as string | null | undefined) ?? null,
      transitionActionName: (v.transitionActionName as string | null | undefined) ?? null,
      detail: (v.detail as string | null | undefined) ?? null,
      // inner dictionary keys stay PascalCase (e.g. UnknownGroupCodes) — preserve verbatim
      metadata: (v.metadata as Record<string, unknown> | null | undefined) ?? null,
    }));
  return violations.length > 0 ? violations : undefined;
}

/** Map an HTTP status + raw response body to a normalized ApiError */
export function normalizeError(status: number, body: unknown): NormalizedApiError {
  if (body && typeof body === 'object') {
    const problem = body as ProblemDetail;
    const envelopeErrors = (body as { errors?: GeneratedApiError[] | null; message?: string | null })?.errors;
    const envelopeMessage = (body as { message?: string | null })?.message;
    const firstBackendError = Array.isArray(envelopeErrors) ? envelopeErrors[0] : undefined;

    // Priority: RFC 9457 detail_vi → backend per-field errors[0] → RFC detail → envelope.message → title → fallback.
    // Per-field message beats envelope-level message so user sees the actual validator failure, not a generic 422 string.
    const message =
      problem.detail_vi ??
      firstBackendError?.detailVi ??
      firstBackendError?.message ??
      problem.detail ??
      envelopeMessage ??
      problem.title ??
      defaultMessage(status);

    const code = problem.code ?? firstBackendError?.code ?? String(status);

    // RFC 9457 errors is a map (object), backend envelope.errors is an array — keep them separate.
    const rfcValidationErrors: Record<string, string[]> | undefined =
      problem.errors && !Array.isArray(problem.errors) && Object.keys(problem.errors).length > 0
        ? problem.errors
        : undefined;
    const envelopeValidationErrors = Array.isArray(envelopeErrors) && envelopeErrors.length > 0
      ? groupErrorsByProperty(envelopeErrors)
      : undefined;

    return {
      status,
      code,
      message,
      detail: problem.detail,
      validationErrors: rfcValidationErrors ?? envelopeValidationErrors,
      violations: extractViolations(firstBackendError),
    };
  }

  return { status, code: String(status), message: defaultMessage(status) };
}

// Vietnamese fallback strings (used when i18next is not yet initialized — e.g. tests, early boot)
const VI_FALLBACKS: Record<number, string> = {
  400: 'Yêu cầu không hợp lệ.',
  401: 'Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.',
  403: 'Bạn không có quyền thực hiện thao tác này.',
  404: 'Không tìm thấy tài nguyên.',
  409: 'Dữ liệu đã bị thay đổi bởi người dùng khác. Vui lòng tải lại trang.',
  422: 'Dữ liệu không hợp lệ. Vui lòng kiểm tra lại.',
  429: 'Quá nhiều yêu cầu. Vui lòng thử lại sau.',
  500: 'Lỗi hệ thống. Vui lòng liên hệ quản trị viên.',
};

function defaultMessage(status: number): string {
  const fallback = VI_FALLBACKS[status] ?? 'Đã xảy ra lỗi không xác định.';
  // Guard: i18next.t returns undefined when not initialized (e.g. unit tests, SSR).
  // Fall back to hardcoded Vietnamese strings in that case.
  if (!i18next.isInitialized) return fallback;
  switch (status) {
    case 400: return i18next.t('error.http.badRequest',      { defaultValue: fallback });
    case 401: return i18next.t('error.http.unauthorized',    { defaultValue: fallback });
    case 403: return i18next.t('error.http.forbidden',       { defaultValue: fallback });
    case 404: return i18next.t('error.http.notFound',        { defaultValue: fallback });
    case 409: return i18next.t('error.http.conflict',        { defaultValue: fallback });
    case 422: return i18next.t('error.http.unprocessable',   { defaultValue: fallback });
    case 429: return i18next.t('error.http.tooManyRequests', { defaultValue: fallback });
    case 500: return i18next.t('error.http.serverError',     { defaultValue: fallback });
    default:  return i18next.t('error.http.unknown',         { defaultValue: fallback });
  }
}

// Minimal structural translate fn — keeps formatGraphViolations decoupled from react-i18next types + unit-testable.
type TranslateFn = (key: string, options?: Record<string, unknown>) => string;

/** Read PascalCase UnknownGroupCodes from a violation's inner metadata dict (BE has no DictionaryKeyPolicy). */
function unknownGroupCodes(metadata: Record<string, unknown> | null | undefined): string[] | undefined {
  const raw = metadata?.['UnknownGroupCodes'];
  return Array.isArray(raw) && raw.length > 0 ? raw.map(String) : undefined;
}

/** Build a Vietnamese multi-line toast from graph violations. Caps the list so a 20-violation save
 * doesn't render an unreadable wall; surfaces UnknownGroupCodes inline when the ByGroup check fired. */
export function formatGraphViolations(violations: GraphViolation[], t: TranslateFn): string {
  const MAX = 5;
  const header = t('workflow.designer.saveFailed', {
    count: violations.length,
    defaultValue: `Lưu thất bại — ${violations.length} vi phạm:`,
  });
  const lines = violations.slice(0, MAX).map((v) => {
    const codes = unknownGroupCodes(v.metadata);
    const detail = codes
      ? t('workflow.designer.unknownGroupCodes', {
          codes: codes.join(', '),
          defaultValue: `Mã nhóm không tồn tại: ${codes.join(', ')}`,
        })
      : (v.detail ?? v.rule);
    // Build label for the bullet: include transitionActionName with an arrow when present.
    // 4 cases: (state+transition) "State → Transition" / (state-only) "State" /
    //          (transition-only) "Transition" / (neither) label = undefined → bare detail.
    const transition = v.transitionActionName ?? undefined;
    const state = v.stateName ?? undefined;
    const label = state && transition
      ? `${state} → ${transition}`
      : (state ?? transition);
    return label ? `• ${label}: ${detail}` : `• ${detail}`;
  });
  if (violations.length > MAX) {
    const extra = violations.length - MAX;
    lines.push(
      t('workflow.designer.moreViolations', {
        count: extra,
        defaultValue: `…và ${extra} vi phạm khác`,
      }),
    );
  }
  return [header, ...lines].join('\n');
}
