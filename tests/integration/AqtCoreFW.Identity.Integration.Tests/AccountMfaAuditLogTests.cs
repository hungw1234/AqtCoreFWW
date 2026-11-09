using AqtCoreFW.Audit.Infrastructure.Persistence;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AqtCoreFW.Identity.Integration.Tests;

/// <summary>
/// Asserts that the AccountMfa state-mutating writes emit an AuditLogEntry (Category=AccountManagement).
///
/// These writes previously ran through a service-locator <c>UserManager</c> mutation in the controller,
/// outside the MediatR pipeline, so <c>AuditBehavior</c> (constrained to <c>IBaseCommand</c>) never fired —
/// an ADR-004 audit gap for a security-sensitive account operation (QĐ 742 §1.6.b). After the retrofit the
/// writes dispatch <c>ICommand</c> handlers; this is the count-0→1 proof that the audit row now lands.
///
/// Complete-enrolment is the success-path write exercised here (it does not require a fresh MFA session);
/// the same AuditBehavior mechanism covers Unenrol/Regenerate, whose success paths require an mfa_at claim.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class AccountMfaAuditLogTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public AccountMfaAuditLogTests(WebAppFixture app) => _app = app;

    [Fact]
    public async Task CompleteEnrol_EmitsAuditLogEntry_WithAccountManagementCategory()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        await SeedUserAsync(userId, tenantId);

        var client = _app.CreateAuthenticatedClient(userId, tenantId);
        var auditDb = GetAuditDb();

        var countBefore = await auditDb.AuditLogEntries
            .Where(e => e.Category == AuditCategory.AccountManagement && e.ResourceType == "CompleteTotpEnrol")
            .CountAsync();

        // Begin enrolment → extract pending secret → compute current TOTP → complete.
        var beginBody = await (await client.GetAsync("/api/v1/account/mfa/enrol/totp"))
            .Content.ReadFromJsonAsync<JsonElement>();
        var qrUri = beginBody.GetProperty("data").GetProperty("qrCodeUri").GetString()!;
        var secret = new Uri(qrUri).Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => p.Length > 1 ? Uri.UnescapeDataString(p[1]) : "")["secret"];

        var currentCode = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp();
        var completeResp = await client.PostAsJsonAsync(
            "/api/v1/account/mfa/enrol/totp", new { TotpCode = currentCode });
        completeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // AuditBehavior runs synchronously inside the MediatR pipeline — no propagation delay.
        var countAfter = await auditDb.AuditLogEntries
            .Where(e => e.Category == AuditCategory.AccountManagement && e.ResourceType == "CompleteTotpEnrol")
            .CountAsync();

        countAfter.Should().BeGreaterThan(countBefore,
            "AuditBehavior must fire for CompleteTotpEnrolCommand (ICommand<T>, AccountManagement) on success — " +
            "the MFA enrolment write previously bypassed the pipeline and produced no audit row");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task SeedUserAsync(Guid userId, Guid tenantId)
    {
        using var scope = _app.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            Id = userId,
            UserName = $"mfa-audit-{userId:N}",
            Email = $"mfa-audit-{userId:N}@test.vn",
            EmailConfirmed = true,
            FullName = "MFA Audit User",
            TenantId = tenantId,
            IsActive = true,
        };
        var result = await userManager.CreateAsync(user, "TestPass@12345!");
        result.Succeeded.Should().BeTrue(
            $"seed failed: {string.Join(", ", result.Errors.Select(e => e.Description))}");
    }

    private AuditDbContext GetAuditDb()
    {
        var scope = _app.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AuditDbContext>();
    }
}
