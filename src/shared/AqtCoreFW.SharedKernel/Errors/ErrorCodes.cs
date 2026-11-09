using FluentResults;

namespace AqtCoreFW.SharedKernel.Errors;

/// <summary>GOV error codes following GOV_MODULE_NNN convention.</summary>
public static class ErrorCodes
{
    public static class Cases
    {
        public const string NotFound = "GOV_CAS_001";
        public const string InvalidStatus = "GOV_CAS_002";
        public const string WorkflowViolation = "GOV_CAS_003";
    }

    public static class Identity
    {
        public const string NotFound = "GOV_IDN_001";
        public const string InvalidCredentials = "GOV_IDN_002";
        public const string MfaRequired = "GOV_IDN_003";
        public const string TokenExpired = "GOV_IDN_004";
        public const string PasswordExpired = "GOV_IDN_005";
    }

    public static class Audit
    {
        public const string ChainBroken = "GOV_AUD_001";
        public const string NotFound = "GOV_AUD_002";
    }

    public static class Files
    {
        public const string NotFound = "GOV_FIL_001";
        public const string VirusScanFailed = "GOV_FIL_002";
        public const string TypeNotAllowed = "GOV_FIL_003";
        public const string SizeExceeded = "GOV_FIL_004";
    }

    public static class Security
    {
        public const string TenantRequired = "GOV_SEC_001";
        public const string Forbidden = "GOV_SEC_002";
        public const string IpBlocked = "GOV_SEC_003";
        public const string MassAssignment = "GOV_SEC_004";
    }

    public static class System
    {
        public const string InternalError = "GOV_SYS_000";
        public const string NotFound = "GOV_SYS_001";
        public const string ValidationFailed = "GOV_SYS_002";
        public const string IdempotencyConflict = "GOV_SYS_003";
    }

    public static class Ai
    {
        public const string SovereigntyViolation = "GOV_AI_001";
        public const string LocalModelUnavailable = "GOV_AI_002";
    }

    public static class Workflow
    {
        /// <summary>GOV_WORKFLOW_040: Cursor token is malformed or cannot be decoded.</summary>
        public const string MalformedCursor = "GOV_WORKFLOW_040";
    }
}

/// <summary>Custom FluentResults error types for HTTP status mapping in ApiControllerBase.</summary>
public class NotFoundError : Error
{
    public NotFoundError(string message) : base(message)
        => this.WithMetadata("ErrorCode", ErrorCodes.System.NotFound);
}

public class ForbiddenError : Error
{
    public ForbiddenError(string message) : base(message)
        => this.WithMetadata("ErrorCode", ErrorCodes.Security.Forbidden);
}

public class ConflictError : Error
{
    public ConflictError(string message) : base(message) { }
}

public class ValidationError : Error
{
    public ValidationError(string message, string property = "") : base(message)
    {
        this.WithMetadata("Property", property);
        this.WithMetadata("ErrorCode", ErrorCodes.System.ValidationFailed);
    }
}

/// <summary>Maps to HTTP 400 Bad Request. Use for malformed input that cannot be parsed (e.g. cursor tokens).</summary>
public class BadRequestError : Error
{
    public BadRequestError(string message, string errorCode) : base(message)
        => this.WithMetadata("ErrorCode", errorCode);
}

/// <summary>
/// Maps to HTTP 412 Precondition Failed. Use for ETag / If-Match concurrency guard failures
/// (RowVersion mismatch). Caller must refresh the resource and retry.
/// </summary>
public class PreconditionFailedError : Error
{
    public PreconditionFailedError(string message) : base(message)
        => this.WithMetadata("ErrorCode", ErrorCodes.System.ValidationFailed);
}

/// <summary>
/// Maps to HTTP 503 Service Unavailable. Use when an external upstream dependency the request relies on is
/// down/unreachable (e.g. a remote signing provider outage) — the failure is NOT the caller's fault and the
/// request is retryable, so a 5xx (not a 4xx) is the correct signal.
/// </summary>
public class ServiceUnavailableError : Error
{
    public ServiceUnavailableError(string message, string errorCode) : base(message)
        => this.WithMetadata("ErrorCode", errorCode);
}
