using System.Security.Cryptography;
using System.Text.Json;
using AqtCoreFW.Identity.Application.Services;
using AqtCoreFW.Identity.Domain.Entities;
using BC = BCrypt.Net.BCrypt;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Identity.Infrastructure.Services;

/// <summary>
/// Generates and validates one-time MFA recovery codes (QĐ742 1.6).
///
/// Code alphabet: A-Z excluding O/I (ambiguous with 0/1) + digits 2-9 = 32 chars.
/// Code length: 8 chars → 32^8 ≈ 1.1e12 space (adequate entropy for one-time codes).
/// Hash: BCrypt work-factor 11 — resistant to offline attack, per plan amendment §1.
/// Storage: JSON array of BCrypt hashes in ApplicationUser.RecoveryCodesHash.
/// Display: plaintext shown ONCE after enrolment — never re-exposed.
/// </summary>
public sealed class RecoveryCodeService : IRecoveryCodeService
{
    private const int CodeCount = 16;
    private const int CodeLength = 8;
    private const int BcryptWorkFactor = 11;

    // Unambiguous uppercase alphabet: A-Z minus O and I; digits 2-9 (no 0 or 1).
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<RecoveryCodeService> _logger;

    public RecoveryCodeService(
        UserManager<ApplicationUser> userManager,
        ILogger<RecoveryCodeService> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<(string[] PlaintextCodes, string HashedJsonArray)> GenerateAsync()
    {
        var codes = new string[CodeCount];
        var hashes = new string[CodeCount];

        for (var i = 0; i < CodeCount; i++)
        {
            codes[i] = GenerateCode();
            hashes[i] = BC.HashPassword(codes[i], BcryptWorkFactor);
        }

        var hashedJson = JsonSerializer.Serialize(hashes);
        return Task.FromResult((codes, hashedJson));
    }

    /// <inheritdoc/>
    public async Task<bool> ValidateAndConsumeAsync(
        Guid userId, string code, CancellationToken ct = default)
    {
        try
        {
            var user = await _userManager.FindByIdAsync(userId.ToString());
            if (user is null)
            {
                _logger.LogWarning("ValidateAndConsume: user {UserId} not found", userId);
                return false;
            }

            if (string.IsNullOrEmpty(user.RecoveryCodesHash))
                return false;

            string[] hashes;
            try
            {
                hashes = JsonSerializer.Deserialize<string[]>(user.RecoveryCodesHash)
                    ?? Array.Empty<string>();
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "ValidateAndConsume: corrupt RecoveryCodesHash for user {UserId}", userId);
                return false;
            }

            // Find the first matching hash (BCrypt.Verify is constant-time per-hash).
            var matchedIndex = -1;
            for (var i = 0; i < hashes.Length; i++)
            {
                if (BC.Verify(code, hashes[i]))
                {
                    matchedIndex = i;
                    break;
                }
            }

            if (matchedIndex < 0)
                return false;

            // Remove the consumed hash and persist — atomic within the single UpdateAsync call.
            var remaining = hashes.Where((_, idx) => idx != matchedIndex).ToArray();
            user.RecoveryCodesHash = JsonSerializer.Serialize(remaining);

            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                _logger.LogError(
                    "ValidateAndConsume: failed to persist hash removal for user {UserId}: {Errors}",
                    userId,
                    string.Join(", ", updateResult.Errors.Select(e => e.Description)));
                return false;
            }

            _logger.LogInformation(
                "Recovery code consumed for user {UserId}. Remaining: {Count}", userId, remaining.Length);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "ValidateAndConsume: unexpected error for user {UserId}", userId);
            return false;
        }
    }

    /// <summary>Generates a single cryptographically random code from the unambiguous alphabet.</summary>
    private static string GenerateCode()
    {
        var chars = new char[CodeLength];
        // Use RandomNumberGenerator for cryptographic quality.
        var bytes = RandomNumberGenerator.GetBytes(CodeLength);
        for (var i = 0; i < CodeLength; i++)
        {
            // Modulo bias is negligible: Alphabet.Length=32, byte=256, 256%32==0 → no bias.
            chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        }
        return new string(chars);
    }
}
