namespace AqtCoreFW.Identity.Application.Services;

/// <summary>MFA operations — TOTP setup/verify, Email OTP dispatch, and enrolment flow.</summary>
public interface IMfaService
{
    Task<string> GenerateTotpSetupAsync(Guid userId);
    Task<bool> ValidateTotpAsync(Guid userId, string code);
    Task SendEmailOtpAsync(Guid userId);

    /// <summary>
    /// Begins TOTP enrolment: generates a new secret, caches it temporarily (10 min TTL),
    /// returns otpauth:// URI for QR code and the base32 secret for manual entry.
    /// Does NOT persist until <see cref="CompleteTotpEnrolAsync"/> is called.
    /// </summary>
    Task<(string QrCodeUri, string SecretBase32)> BeginTotpEnrolAsync(Guid userId);

    /// <summary>
    /// Completes TOTP enrolment: validates <paramref name="totpCode"/> against the cached secret,
    /// persists <see cref="Domain.Entities.ApplicationUser.TotpSecret"/> and
    /// <see cref="Domain.Entities.ApplicationUser.TotpEnrolledAt"/>, clears the cache entry.
    /// Returns true on success, false if code is invalid or no pending enrolment exists.
    /// </summary>
    Task<bool> CompleteTotpEnrolAsync(Guid userId, string totpCode);

    /// <summary>
    /// Reads the current TOTP enrolment status for a user. Returns null when the user does not exist
    /// (the caller maps that to 401). Keeps the read off the controller so AccountMfa does not load the
    /// user entity itself (no service-locator UserManager pull).
    /// </summary>
    Task<MfaEnrolmentStatus?> GetEnrolmentStatusAsync(Guid userId);
}

/// <summary>TOTP enrolment status for the current user.</summary>
public sealed record MfaEnrolmentStatus(bool Enrolled, DateTime? EnrolledAt);
