namespace AqtCoreFW.Audit.Domain.Services;

/// <summary>
/// Primary audit service interface — all modules use this to log compliance events.
/// HMAC chain computed asynchronously (Hangfire) to avoid blocking the write path.
/// </summary>
public interface IAuditService
{
    Task LogAsync(
        string action,
        string moduleName,
        string resourceType,
        string? resourceId = null,
        string? dataSnapshot = null,
        CancellationToken cancellationToken = default);

    Task LogPersonalDataAccessAsync(
        Guid dataSubjectId,
        string dataCategory,
        string purpose,
        string legalBasis,
        CancellationToken cancellationToken = default);

    Task LogLoginAttemptAsync(
        Guid? userId, string email, string ipAddress, string? userAgent,
        bool success, string? failureReason = null,
        CancellationToken cancellationToken = default);
}
