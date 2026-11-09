using AqtCoreFW.Identity.Application.Queries.ListUsers;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AqtCoreFW.Identity.Integration.Tests;

/// <summary>
/// Real-DB proof that read-model paths decrypt the AES-GCM encrypted AspNetUsers.Email column.
///
/// Email is encrypted at rest; Dapper read paths (the cross-module client + the admin list query)
/// bypass the EF value converter, so without an explicit decrypt they would surface ciphertext
/// (the "Email không hợp lệ" edit-form bug + ciphertext in the user list). These tests seed via EF
/// (encrypting Email), then assert both read paths return PLAINTEXT — non-vacuous because the
/// WebAppFixture wires the live IKeyResolver, so a missing decrypt would fail with a v{N}: value.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class IdentityReadModelEmailDecryptionTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public IdentityReadModelEmailDecryptionTests(WebAppFixture app)
    {
        _app = app;
    }

    [Fact]
    public async Task GetUserInfoByIdsAsync_ReturnsDecryptedEmail()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var email = $"client.decrypt.{userId:N}@example.vn";
        await SeedUserAsync(userId, tenantId, email);

        var map = await WithScopeAsync(sp =>
            sp.GetRequiredService<IIdentityModuleClient>().GetUserInfoByIdsAsync(new[] { userId }, tenantId));

        map.Should().ContainKey(userId);
        map[userId].Email.Should().Be(email,
            because: "the cross-module client must decrypt Email so enrichment callers display plaintext");
        map[userId].Email.Should().NotStartWith("v",
            because: "a decrypted email must not carry the v{N}: ciphertext prefix");
    }

    [Fact]
    public async Task ListUsersQuery_ReturnsDecryptedEmail()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var email = $"list.decrypt.{userId:N}@example.vn";
        await SeedUserAsync(userId, tenantId, email);

        var result = await WithScopeAsync(sp =>
            sp.GetRequiredService<ISender>().Send(new ListUsersQuery(tenantId, null, null, null)));

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value.Items.Should().ContainSingle(u => u.Id == userId).Subject;
        dto.Email.Should().Be(email,
            because: "the admin user list must decrypt Email so the edit form prefills a valid address");
    }

    // --- Helpers ---

    private async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private Task SeedUserAsync(Guid userId, Guid tenantId, string email)
    {
        return WithScopeAsync<bool>(async sp =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            db.Users.Add(new ApplicationUser
            {
                Id = userId,
                UserName = email,
                Email = email, // encrypted at rest by the EF converter on SaveChanges
                NormalizedEmail = email.ToUpperInvariant(),
                NormalizedUserName = email.ToUpperInvariant(),
                SecurityStamp = Guid.NewGuid().ToString(),
                ConcurrencyStamp = Guid.NewGuid().ToString(),
                FullName = "Email decrypt test user",
                IsActive = true,
                TenantId = tenantId,
            });
            await db.SaveChangesAsync();
            return true;
        });
    }
}
