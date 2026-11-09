namespace AqtCoreFW.Identity.Application.Services;

/// <summary>
/// Recovery code operations for MFA enrolment (QĐ742 1.6).
/// 16 one-time codes per user, BCrypt-hashed at rest (work factor 11).
/// Codes display once at enrolment — never re-exposed after that.
/// </summary>
public interface IRecoveryCodeService
{
    /// <summary>
    /// Generates 16 cryptographically random 8-char alphanumeric codes.
    /// Returns (plaintext[], hashedJsonArray) — caller displays plaintext once,
    /// stores hashedJsonArray in ApplicationUser.RecoveryCodesHash.
    /// </summary>
    Task<(string[] PlaintextCodes, string HashedJsonArray)> GenerateAsync();

    /// <summary>
    /// Validates <paramref name="code"/> against the stored hashes for <paramref name="userId"/>.
    /// On match: removes the consumed hash from storage (one-time use), persists update.
    /// Returns true if code was valid and consumed, false otherwise.
    /// Never throws on invalid input — returns false.
    /// </summary>
    Task<bool> ValidateAndConsumeAsync(Guid userId, string code, CancellationToken ct = default);
}
