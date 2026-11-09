namespace AqtCoreFW.Identity.Infrastructure.Services.Signing.ViettelMysign;

/// <summary>
/// Raised when the Viettel Mysign Login endpoint returns an application-level error
/// (HTTP 200 with <c>error</c> field, or non-2xx HTTP status).
/// Callers should NOT retry on this exception — it represents a credential or config problem.
/// </summary>
internal sealed class MysignLoginException(string message) : Exception(message);

/// <summary>
/// Raised when the Viettel Mysign CertificateInfo endpoint returns no usable certificate
/// (empty array, all certs disabled, or application-level error body).
/// </summary>
internal sealed class MysignCertInfoException(string message) : Exception(message);

/// <summary>
/// Raised when the Viettel Mysign SignHash endpoint returns an application-level error
/// (HTTP 200 with <c>error</c> field, HTTP 4xx, or empty signatures array).
/// NOT raised on HTTP 5xx or timeout — those surface as <see cref="HttpRequestException"/>
/// or <see cref="TaskCanceledException"/> from the HttpClient layer.
/// </summary>
internal sealed class MysignSignHashException(string message) : Exception(message);
