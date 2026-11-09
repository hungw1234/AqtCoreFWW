// Surface the actual backend failure reason at a mutation/query catch-site instead of a
// generic toast. Two error shapes can reach a `.catch`/`try` block depending on the call path:
//
//   1. NormalizedApiError — the axios interceptor (api-client) rejects with this for most
//      calls: { status, code, message } where `message` is Vietnamese-first.
//   2. Raw axios error — some call paths surface the un-normalized envelope:
//      { response: { data: { errors: [{ detailVi, message, code }], message } } }.
//
// These helpers read whichever shape is present and never throw. An antd `form.validateFields()`
// rejection ({ errorFields, values }) matches neither → both return undefined, so callers can
// skip the toast and let antd render inline field errors.

interface RawEnvelopeError {
  detailVi?: unknown;
  message?: unknown;
  code?: unknown;
}

interface ErrorLikeShape {
  status?: unknown;
  code?: unknown;
  message?: unknown;
  response?: { data?: { message?: unknown; errors?: RawEnvelopeError[] } };
}

function asObject(err: unknown): ErrorLikeShape | undefined {
  return err && typeof err === 'object' ? (err as ErrorLikeShape) : undefined;
}

function nonEmptyString(value: unknown): string | undefined {
  return typeof value === 'string' && value.length > 0 ? value : undefined;
}

/**
 * Extract a human-readable backend message from either error shape.
 * Returns undefined for non-API rejections (e.g. antd validation) so the caller can stay silent.
 */
export function extractApiErrorMessage(err: unknown): string | undefined {
  const e = asObject(err);
  if (!e) return undefined;

  // Raw envelope first: per-field detailVi → per-field message → envelope-level message.
  const firstEnvelopeError = e.response?.data?.errors?.[0];
  const fromEnvelope =
    nonEmptyString(firstEnvelopeError?.detailVi) ??
    nonEmptyString(firstEnvelopeError?.message) ??
    nonEmptyString(e.response?.data?.message);
  if (fromEnvelope) return fromEnvelope;

  // NormalizedApiError: only trust `.message` when the object is API-error-shaped (has code/status),
  // so a bare JS Error's internal message is never surfaced to the user.
  if (('code' in e || 'status' in e) && nonEmptyString(e.message)) {
    return e.message as string;
  }
  return undefined;
}

/** Extract the BE error code (GOV_* or HTTP status string) from either shape, if present. */
export function extractApiErrorCode(err: unknown): string | undefined {
  const e = asObject(err);
  if (!e) return undefined;
  return (
    nonEmptyString(e.response?.data?.errors?.[0]?.code) ?? nonEmptyString(e.code)
  );
}
