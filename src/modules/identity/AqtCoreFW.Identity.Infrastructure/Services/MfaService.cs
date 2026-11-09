using AqtCoreFW.Identity.Application.Services;
using AqtCoreFW.Identity.Domain.Entities;
using Hangfire;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using OtpNet;

namespace AqtCoreFW.Identity.Infrastructure.Services;

/// <summary>
/// MFA service supporting TOTP (OtpNet, 30s window) and Email OTP (dispatched via Hangfire).
/// Also handles TOTP enrolment flow: BeginTotpEnrolAsync caches pending secret in IMemoryCache
/// (10 min TTL); CompleteTotpEnrolAsync verifies, then dual-writes per ADR-007:
/// AspNetUserTokens (Identity primary store) + ApplicationUser.TotpSecret (encrypted backup).
/// </summary>
public sealed class MfaService : IMfaService
{
    private const string TotpIssuer = "AqtCoreFW";
    private const int TotpWindowSize = 1; // ±1 step tolerance (30s each side)
    private const string EnrolCacheKeyPrefix = "mfa:enrol:pending:";
    private static readonly TimeSpan EnrolCacheTtl = TimeSpan.FromMinutes(10);

    // Internal token names used by UserStore<TUser>'s IUserAuthenticatorKeyStore implementation
    // (AspNetUserTokens row with LoginProvider="[AspNetUserStore]" + Name="AuthenticatorKey").
    // We write through SetAuthenticationTokenAsync because UserManager only exposes
    // ResetAuthenticatorKeyAsync (random key) — we need to set a specific user-verified secret.
    private const string AuthenticatorStoreLoginProvider = "[AspNetUserStore]";
    private const string AuthenticatorKeyTokenName = "AuthenticatorKey";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IBackgroundJobClient _jobs;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MfaService> _logger;

    public MfaService(
        UserManager<ApplicationUser> userManager,
        IBackgroundJobClient jobs,
        IMemoryCache cache,
        ILogger<MfaService> logger)
    {
        _userManager = userManager;
        _jobs = jobs;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Generates or resets TOTP secret for user.
    /// Returns an otpauth:// URI suitable for QR code rendering by the client.
    /// </summary>
    public async Task<string> GenerateTotpSetupAsync(Guid userId)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException($"User {userId} not found.");

        // Reset the authenticator key — generates a new base32 secret
        await _userManager.ResetAuthenticatorKeyAsync(user);
        var rawKey = await _userManager.GetAuthenticatorKeyAsync(user);

        if (string.IsNullOrEmpty(rawKey))
            throw new InvalidOperationException("Failed to generate authenticator key.");

        // Build standard otpauth URI
        var encodedIssuer = Uri.EscapeDataString(TotpIssuer);
        var encodedAccount = Uri.EscapeDataString(user.Email ?? user.UserName ?? userId.ToString());
        var uri = $"otpauth://totp/{encodedIssuer}:{encodedAccount}?secret={rawKey}&issuer={encodedIssuer}&algorithm=SHA1&digits=6&period=30";

        _logger.LogInformation("TOTP setup generated for user {UserId}", userId);
        return uri;
    }

    /// <summary>
    /// Validates a TOTP code against the user's stored authenticator key.
    /// Accepts ±1 time step (30s tolerance).
    /// </summary>
    public async Task<bool> ValidateTotpAsync(Guid userId, string code)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null) return false;

        var rawKey = await _userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(rawKey)) return false;

        var keyBytes = Base32Encoding.ToBytes(rawKey);
        var totp = new Totp(keyBytes);

        // VerifyTotp with window validates current step ± TotpWindowSize steps
        var valid = totp.VerifyTotp(
            totp: code,
            timeStepMatched: out _,
            window: new VerificationWindow(TotpWindowSize, TotpWindowSize));

        _logger.LogInformation("TOTP validation for user {UserId}: {Result}", userId, valid ? "success" : "failed");
        return valid;
    }

    /// <summary>
    /// Enqueues a Hangfire job to send a 6-digit OTP to the user's registered email.
    /// The job itself generates the OTP and sends the email — keeping this method fast.
    /// </summary>
    public async Task SendEmailOtpAsync(Guid userId)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            _logger.LogWarning("SendEmailOtp called for non-existent user {UserId}", userId);
            return;
        }

        var email = user.Email;
        if (string.IsNullOrEmpty(email))
        {
            _logger.LogWarning("User {UserId} has no email — cannot send OTP", userId);
            return;
        }

        // Enqueue to Hangfire — job sends OTP email asynchronously
        _jobs.Enqueue<EmailOtpJob>(job => job.SendAsync(userId, email, CancellationToken.None));

        _logger.LogInformation("Email OTP job enqueued for user {UserId}", userId);
    }

    /// <summary>
    /// Begins TOTP enrolment: generates a fresh base32 secret, stores it in IMemoryCache
    /// with a 10-minute TTL (not yet persisted to DB), and returns the otpauth:// URI + secret.
    /// Calling again within TTL overwrites the pending secret — each call generates a new one.
    /// </summary>
    public async Task<(string QrCodeUri, string SecretBase32)> BeginTotpEnrolAsync(Guid userId)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException($"User {userId} not found.");

        // Generate a new 20-byte (160-bit) TOTP secret — OtpNet standard size.
        var secretBytes = KeyGeneration.GenerateRandomKey(20);
        var secretBase32 = Base32Encoding.ToString(secretBytes);

        // Cache the pending secret — overwrite if called again (re-enrol attempt).
        // Size=1 ensures compatibility with IMemoryCache instances configured with SizeLimit
        // (test fixtures, some prod configs). Without Size the Set call throws when SizeLimit is set.
        var cacheKey = EnrolCacheKeyPrefix + userId;
        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = EnrolCacheTtl,
            Size = 1,
        };
        _cache.Set(cacheKey, secretBase32, cacheOptions);

        var encodedIssuer = Uri.EscapeDataString(TotpIssuer);
        var encodedAccount = Uri.EscapeDataString(user.Email ?? user.UserName ?? userId.ToString());
        var qrUri = $"otpauth://totp/{encodedIssuer}:{encodedAccount}" +
                    $"?secret={secretBase32}&issuer={encodedIssuer}&algorithm=SHA1&digits=6&period=30";

        _logger.LogInformation("TOTP enrolment begun for user {UserId} (TTL 10min)", userId);
        return (qrUri, secretBase32);
    }

    /// <summary>
    /// Completes TOTP enrolment: validates <paramref name="totpCode"/> against the cached
    /// pending secret, then persists TotpSecret + TotpEnrolledAt on success.
    /// Clears the cache entry on both success and invalid-code (prevents replay).
    /// Returns false if no pending enrolment exists or code is invalid.
    /// </summary>
    public async Task<bool> CompleteTotpEnrolAsync(Guid userId, string totpCode)
    {
        var cacheKey = EnrolCacheKeyPrefix + userId;
        if (!_cache.TryGetValue(cacheKey, out string? pendingSecret)
            || string.IsNullOrEmpty(pendingSecret))
        {
            _logger.LogWarning("CompleteTotpEnrol: no pending enrolment for user {UserId}", userId);
            return false;
        }

        // Validate code against the cached (not yet persisted) secret.
        var keyBytes = Base32Encoding.ToBytes(pendingSecret);
        var totp = new Totp(keyBytes);
        var valid = totp.VerifyTotp(
            totp: totpCode,
            timeStepMatched: out _,
            window: new VerificationWindow(TotpWindowSize, TotpWindowSize));

        // Always remove from cache — prevents replaying the same code against same pending secret.
        _cache.Remove(cacheKey);

        if (!valid)
        {
            _logger.LogWarning("CompleteTotpEnrol: invalid code for user {UserId}", userId);
            return false;
        }

        // Persist secret + enrollment timestamp to ApplicationUser.
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            _logger.LogError("CompleteTotpEnrol: user {UserId} disappeared after validation", userId);
            return false;
        }

        // Dual-write per ADR-007: AspNetUserTokens (Identity-native, validated by
        // SignInManager.TwoFactorAuthenticatorSignInAsync + UserManager.GetAuthenticatorKeyAsync)
        // is the PRIMARY store; ApplicationUser.TotpSecret is encrypted backup for audit/key-rotation.
        // SetAuthenticationTokenAsync writes through to the same row that GetAuthenticatorKeyAsync reads.
        var setKeyResult = await _userManager.SetAuthenticationTokenAsync(
            user, AuthenticatorStoreLoginProvider, AuthenticatorKeyTokenName, pendingSecret);
        if (!setKeyResult.Succeeded)
        {
            _logger.LogError(
                "CompleteTotpEnrol: SetAuthenticationTokenAsync(AuthenticatorKey) failed for user {UserId}: {Errors}",
                userId,
                string.Join(", ", setKeyResult.Errors.Select(e => e.Description)));
            return false;
        }

        var enable2FaResult = await _userManager.SetTwoFactorEnabledAsync(user, true);
        if (!enable2FaResult.Succeeded)
        {
            _logger.LogError(
                "CompleteTotpEnrol: SetTwoFactorEnabledAsync failed for user {UserId}: {Errors}",
                userId,
                string.Join(", ", enable2FaResult.Errors.Select(e => e.Description)));
            return false;
        }

        user.TotpSecret = pendingSecret; // Encrypted at rest via EncryptedStringConverter in EF config — backup column per ADR-007
        user.TotpEnrolledAt = DateTime.UtcNow;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            _logger.LogError(
                "CompleteTotpEnrol: UpdateAsync failed for user {UserId}: {Errors}",
                userId,
                string.Join(", ", result.Errors.Select(e => e.Description)));
            return false;
        }

        _logger.LogInformation("TOTP enrolment completed for user {UserId}", userId);
        return true;
    }

    /// <summary>
    /// Reads TOTP enrolment status from <see cref="ApplicationUser"/>. Enrolment is the same predicate
    /// the controller used inline before this read moved into the service: a persisted TotpSecret AND a
    /// TotpEnrolledAt timestamp. Returns null when the user is not found.
    /// </summary>
    public async Task<MfaEnrolmentStatus?> GetEnrolmentStatusAsync(Guid userId)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null) return null;

        var enrolled = !string.IsNullOrEmpty(user.TotpSecret) && user.TotpEnrolledAt.HasValue;
        return new MfaEnrolmentStatus(enrolled, user.TotpEnrolledAt);
    }
}

/// <summary>
/// Hangfire job: generates OTP token via Identity token provider and sends via email.
/// Kept in same file to avoid unnecessary file proliferation (YAGNI).
/// </summary>
public sealed class EmailOtpJob
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<EmailOtpJob> _logger;

    public EmailOtpJob(UserManager<ApplicationUser> userManager, ILogger<EmailOtpJob> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    public async Task SendAsync(Guid userId, string email, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null) return;

        // Generate a short-lived token using the "Email" purpose
        var token = await _userManager.GenerateUserTokenAsync(
            user, TokenOptions.DefaultEmailProvider, "EmailOtp");

        // In production: inject IEmailSender and send the token (Phase 07b)
        // Token intentionally omitted from log to prevent credential exposure via log sinks
        _logger.LogDebug("Email OTP generated for {Email} (token omitted from log)", email);
    }
}
