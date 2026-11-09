using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AqtCoreFW.Identity.Integration.Tests;

/// <summary>
/// Real-DB proof that the cross-module phone lookup decrypts the AES-GCM encrypted
/// AspNetUsers.PhoneNumber column.
///
/// PhoneNumber is encrypted at rest by the same EncryptedStringConverter as Email, and this Dapper
/// read path bypasses the EF value converter — so without an explicit decrypt the caller would
/// receive ciphertext and post it to an SMS provider as the destination number. These tests seed via
/// EF (encrypting on write), then assert plaintext comes back. Non-vacuous because the WebAppFixture
/// wires the live IKeyResolver: a missing decrypt would surface a v{N}: prefixed value.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class IdentityReadModelPhoneDecryptionTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public IdentityReadModelPhoneDecryptionTests(WebAppFixture app)
    {
        _app = app;
    }

    [Fact]
    public async Task GetUserPhoneNumbersByIdsAsync_ReturnsDecryptedPhoneNumber()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        const string phoneNumber = "+84901234567";
        await SeedUserAsync(userId, tenantId, phoneNumber);

        var map = await WithScopeAsync(sp =>
            sp.GetRequiredService<IIdentityModuleClient>()
                .GetUserPhoneNumbersByIdsAsync(new[] { userId }, tenantId));

        map.Should().ContainKey(userId);
        map[userId].Should().Be(phoneNumber,
            because: "the SMS dispatch path must receive a dialable number, not stored ciphertext");
        map[userId].Should().NotStartWith("v",
            because: "a decrypted phone number must not carry the v{N}: ciphertext prefix");
    }

    [Fact]
    public async Task GetUserPhoneNumbersByIdsAsync_UserWithoutPhoneNumber_MapsToNull()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await SeedUserAsync(userId, tenantId, phoneNumber: null);

        var map = await WithScopeAsync(sp =>
            sp.GetRequiredService<IIdentityModuleClient>()
                .GetUserPhoneNumbersByIdsAsync(new[] { userId }, tenantId));

        map.Should().ContainKey(userId,
            because: "an existing user without a number is a different case from a missing user");
        map[userId].Should().BeNull();
    }

    // --- Helpers ---

    private async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private Task SeedUserAsync(Guid userId, Guid tenantId, string? phoneNumber)
    {
        var email = $"phone.decrypt.{userId:N}@example.vn";

        return WithScopeAsync<bool>(async sp =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            db.Users.Add(new ApplicationUser
            {
                Id = userId,
                UserName = email,
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                NormalizedUserName = email.ToUpperInvariant(),
                PhoneNumber = phoneNumber, // encrypted at rest by the EF converter on SaveChanges
                SecurityStamp = Guid.NewGuid().ToString(),
                ConcurrencyStamp = Guid.NewGuid().ToString(),
                FullName = "Phone decrypt test user",
                IsActive = true,
                TenantId = tenantId,
            });
            await db.SaveChangesAsync();
            return true;
        });
    }
}
