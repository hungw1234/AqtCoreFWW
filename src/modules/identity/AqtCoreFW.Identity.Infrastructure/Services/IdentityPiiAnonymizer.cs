using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.SharedKernel.Contracts.Rtbf;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Identity.Infrastructure.Services;

/// <summary>
/// RTBF anonymizer for Identity module — replaces user PII via UserManager.
/// Uses UserManager (not raw SQL) to respect concurrency stamps and Identity normalizers.
/// Placeholder email: anonymized_{8hex}@deleted.local (unique, non-resolvable).
/// IsActive set to false to prevent further login after RTBF.
/// Idempotent: skips users whose email already matches the anonymized pattern.
/// </summary>
public sealed class IdentityPiiAnonymizer(
    UserManager<ApplicationUser> userManager,
    ILogger<IdentityPiiAnonymizer> logger) : IModulePiiAnonymizer
{
    public string ModuleName => "Identity";

    public async Task<RtbfAnonymizationResult> AnonymizeAsync(
        Guid dataSubjectId,
        Guid tenantId,
        string? citizenNationalId,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(dataSubjectId.ToString());
        if (user is null)
        {
            logger.LogInformation("RTBF Identity: user {UserId} not found — skipping", dataSubjectId);
            return RtbfAnonymizationResult.Ok(ModuleName, 0);
        }

        // Idempotent check: already anonymized
        if (user.Email?.EndsWith("@deleted.local") == true)
        {
            logger.LogInformation("RTBF Identity: user {UserId} already anonymized — skipping", dataSubjectId);
            return RtbfAnonymizationResult.Ok(ModuleName, 0);
        }

        var hex8 = dataSubjectId.ToString("N")[..8];
        user.FullName = "[DA AN DANH]";
        user.UserName = $"deleted_{hex8}";
        user.Email = $"anonymized_{hex8}@deleted.local";
        user.NormalizedEmail = $"ANONYMIZED_{hex8.ToUpper()}@DELETED.LOCAL";
        user.PhoneNumber = null;
        user.DepartmentCode = null;
        user.IsActive = false;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            return RtbfAnonymizationResult.Fail(ModuleName, errors);
        }

        logger.LogInformation("RTBF Identity: anonymized user {UserId}", dataSubjectId);
        return RtbfAnonymizationResult.Ok(ModuleName, 1);
    }
}
