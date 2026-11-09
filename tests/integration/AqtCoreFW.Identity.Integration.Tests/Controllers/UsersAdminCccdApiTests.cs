using System.Net.Http.Json;
using System.Text.Json;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AqtCoreFW.Identity.Integration.Tests.Controllers;

/// <summary>
/// HTTP-level test for the signing CCCD on the admin user-detail read. Verifies that
/// GET /api/v1/admin/users/{id} surfaces the decrypted SigningNationalId to a privileged caller
/// (end-to-end: encrypted column → EF decrypt → query handler → controller → JSON envelope).
///
/// The View-only nulling branch (caller without IDENTITY.USER.UPDATE) is covered by code review +
/// the controller branch; a dedicated integration test for it needs a View-but-not-Update role
/// seeded in the test DB (the test auth shortcut only models SystemAdmin/full-bypass) — filed as a
/// follow-up rather than built here.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class UsersAdminCccdApiTests : IClassFixture<WebAppFixture>
{
    private const string SampleCccd = "017091004878";

    private readonly WebAppFixture _app;

    public UsersAdminCccdApiTests(WebAppFixture app)
    {
        _app = app;
    }

    [Fact]
    public async Task GetUserById_PrivilegedCaller_ReturnsDecryptedCccd()
    {
        var userId = await SeedUserWithCccdAsync(SampleCccd);

        // SystemAdmin bypasses the permission service → satisfies both User.View (class policy) and
        // the User.Update field-gate, so the CCCD is surfaced.
        var client = _app.CreateAuthenticatedClient(
            Guid.NewGuid(), Guid.NewGuid(), includeMfaClaim: true, "SystemAdmin");

        var response = await client.GetAsync($"/api/v1/admin/users/{userId}");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        // ApiResponse envelope: { success, data, errors }
        var data = json.GetProperty("data");
        data.GetProperty("signingNationalId").GetString().Should().Be(SampleCccd,
            because: "a privileged caller must receive the decrypted CCCD to prefill the edit form");
    }

    private async Task<Guid> SeedUserWithCccdAsync(string cccd)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var tag = Guid.NewGuid().ToString("N");
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"cccd.api.{tag}@example.vn",
            Email = $"cccd.api.{tag}@example.vn",
            NormalizedEmail = $"CCCD.API.{tag}@EXAMPLE.VN",
            NormalizedUserName = $"CCCD.API.{tag}@EXAMPLE.VN",
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            FullName = "CCCD Api User",
            IsActive = true,
            SigningNationalId = cccd,
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return user.Id;
    }
}
