using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Identity;

/// <summary>
/// Real-SQL guard for EF Core's store-default <c>sentinel</c> on INSERT.
///
/// A property configured with <c>HasDefaultValueSql</c> whose sentinel equals its CLR default makes
/// EF omit the column whenever the caller sets that CLR-default value — the DB <c>DEFAULT</c> then
/// silently overrides the caller's explicit choice. Each fact drives a real INSERT then reloads the
/// persisted row (change-tracker cleared) to prove the caller's value survived. Locks two fixes:
/// <list type="bullet">
///   <item>UserSignatureProfile.ShowSerial / ShowIssuer (DB default 1) — HasSentinel(true), commit a9792cc7.</item>
///   <item>MfaPolicy.GraceDays (DB default 7) — HasSentinel(-1), this slice.</item>
/// </list>
/// Non-vacuous: revert either HasSentinel and the reload returns the DB default (true / 7), not the
/// caller's value (false / 0). An in-memory provider cannot exercise this — the sentinel/store-default
/// omission only occurs against a real relational INSERT, hence Testcontainers SQL.
/// </summary>
[Collection("Integration")]
public class EfStoreDefaultSentinelInsertPathTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    [Fact]
    public async Task UserSignatureProfile_InsertedWithBothTogglesFalse_PersistsFalse_NotDbDefaultTrue()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var dbCtx = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        // No FK to Users — random Guids keep the row self-contained.
        var profile = UserSignatureProfile.Create(
            gen: gen,
            tenantId: Guid.NewGuid(),
            userId: Guid.NewGuid(),
            displayName: "Sentinel Insert Test",
            createdBy: Guid.NewGuid(),
            showSerial: false,
            showIssuer: false);
        var publicId = profile.PublicId;

        try
        {
            dbCtx.UserSignatureProfiles.Add(profile);
            await dbCtx.SaveChangesAsync();

            // Drop the tracked in-memory instance so the assertion reads the actual persisted row —
            // this is what exercises the INSERT-path sentinel rather than the value we just set.
            dbCtx.ChangeTracker.Clear();

            var reloaded = await dbCtx.UserSignatureProfiles
                .AsNoTracking()
                .SingleAsync(p => p.PublicId == publicId);

            reloaded.ShowSerial.Should().BeFalse(
                "explicit showSerial:false must survive INSERT — pre-fix EF dropped it and DB default 1 applied");
            reloaded.ShowIssuer.Should().BeFalse(
                "explicit showIssuer:false must survive INSERT — pre-fix EF dropped it and DB default 1 applied");
        }
        finally
        {
            await dbCtx.UserSignatureProfiles
                .Where(p => p.PublicId == publicId)
                .ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task MfaPolicy_InsertedWithGraceDaysZero_PersistsZero_NotDbDefaultSeven()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var dbCtx = sp.GetRequiredService<IdentityDbContext>();
        var gen = sp.GetRequiredService<IGuidGenerator>();

        // Role carries a UNIQUE index — a random key avoids the seeded SystemAdmin/Admin/GovOfficer rows.
        var role = $"ZZ_SENTINEL_{Guid.NewGuid():N}";
        var policy = MfaPolicy.Create(
            gen,
            role,
            isMandatory: true,
            graceDays: 0,
            allowedFactors: MfaFactor.Totp,
            createdBy: Guid.NewGuid());
        var publicId = policy.PublicId;

        try
        {
            dbCtx.MfaPolicies.Add(policy);
            await dbCtx.SaveChangesAsync();

            dbCtx.ChangeTracker.Clear();

            var reloaded = await dbCtx.MfaPolicies
                .AsNoTracking()
                .SingleAsync(p => p.PublicId == publicId);

            reloaded.GraceDays.Should().Be(0,
                "a zero-grace 'must enrol immediately' policy must survive INSERT — pre-fix EF dropped the 0 "
                + "and DB default 7 applied, silently downgrading the security posture");
        }
        finally
        {
            await dbCtx.MfaPolicies
                .Where(p => p.PublicId == publicId)
                .ExecuteDeleteAsync();
        }
    }
}
