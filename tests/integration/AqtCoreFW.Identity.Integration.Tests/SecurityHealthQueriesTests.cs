using AqtCoreFW.Identity.Application.SecurityHealth.Queries;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using System.Net;

namespace AqtCoreFW.Identity.Integration.Tests;

[Collection(SqlServerCollection.CollectionName)]
public sealed class SecurityHealthQueriesTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;
    private static readonly Guid SystemAdminUserId = Guid.NewGuid();
    private static readonly Guid RegularUserId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();

    public SecurityHealthQueriesTests(WebAppFixture app) => _app = app;

    private HttpClient CreateSystemAdminClient() =>
        _app.CreateAuthenticatedClient(SystemAdminUserId, TenantId, includeMfaClaim: true, "SystemAdmin");

    private HttpClient CreateRegularUserClient() =>
        _app.CreateAuthenticatedClient(RegularUserId, TenantId, includeMfaClaim: true);

    private HttpClient CreateAnonymousClient() => _app.CreateClient();

    private HttpClient CreateMfalessClient() =>
        _app.CreateAuthenticatedClient(SystemAdminUserId, TenantId, includeMfaClaim: false, "SystemAdmin");

    [Fact]
    public async Task GetLoginPolicyStatus_SystemAdmin_Returns200() =>
        (await CreateSystemAdminClient().GetAsync("/api/v1/admin/security/health/login-policy"))
        .StatusCode.Should().Be(HttpStatusCode.OK);

    [Fact]
    public async Task GetLoginPolicyStatus_RegularUser_Returns403() =>
        (await CreateRegularUserClient().GetAsync("/api/v1/admin/security/health/login-policy"))
        .StatusCode.Should().Be(HttpStatusCode.Forbidden);

    [Fact]
    public async Task GetLoginPolicyStatus_Anonymous_Returns401() =>
        (await CreateAnonymousClient().GetAsync("/api/v1/admin/security/health/login-policy"))
        .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task GetPasswordPolicyStatus_SystemAdmin_Returns200() =>
        (await CreateSystemAdminClient().GetAsync("/api/v1/admin/security/health/password-policy"))
        .StatusCode.Should().Be(HttpStatusCode.OK);

    [Fact]
    public async Task GetPasswordPolicyStatus_RegularUser_Returns403() =>
        (await CreateRegularUserClient().GetAsync("/api/v1/admin/security/health/password-policy"))
        .StatusCode.Should().Be(HttpStatusCode.Forbidden);

    [Fact]
    public async Task GetAuditRetentionStatus_SystemAdmin_Returns200() =>
        (await CreateSystemAdminClient().GetAsync("/api/v1/admin/security/health/audit-retention"))
        .StatusCode.Should().Be(HttpStatusCode.OK);

    [Fact]
    public async Task GetAuditRetentionStatus_RegularUser_Returns403() =>
        (await CreateRegularUserClient().GetAsync("/api/v1/admin/security/health/audit-retention"))
        .StatusCode.Should().Be(HttpStatusCode.Forbidden);

    [Fact]
    public async Task GetAuditIntegrityStatus_SystemAdmin_Returns200() =>
        (await CreateSystemAdminClient().GetAsync("/api/v1/admin/security/health/audit-integrity"))
        .StatusCode.Should().Be(HttpStatusCode.OK);

    [Fact]
    public async Task GetAuditIntegrityStatus_NoMfaClaim_Returns403() =>
        (await CreateMfalessClient().GetAsync("/api/v1/admin/security/health/audit-integrity"))
        .StatusCode.Should().Be(HttpStatusCode.Forbidden);

    [Fact]
    public async Task GetKeyRotationStatus_SystemAdmin_Returns200() =>
        (await CreateSystemAdminClient().GetAsync("/api/v1/admin/security/health/key-rotation"))
        .StatusCode.Should().Be(HttpStatusCode.OK);

    [Fact]
    public async Task GetKeyRotationStatus_RegularUser_Returns403() =>
        (await CreateRegularUserClient().GetAsync("/api/v1/admin/security/health/key-rotation"))
        .StatusCode.Should().Be(HttpStatusCode.Forbidden);

    [Fact]
    public async Task GetBackupHistoryStatus_SystemAdmin_Returns200() =>
        (await CreateSystemAdminClient().GetAsync("/api/v1/admin/security/health/backup-history"))
        .StatusCode.Should().Be(HttpStatusCode.OK);

    [Fact]
    public async Task GetBackupHistoryStatus_NoMfaClaim_Returns403() =>
        (await CreateMfalessClient().GetAsync("/api/v1/admin/security/health/backup-history"))
        .StatusCode.Should().Be(HttpStatusCode.Forbidden);

    [Theory]
    [InlineData("/api/v1/admin/security/health/login-policy")]
    [InlineData("/api/v1/admin/security/health/password-policy")]
    [InlineData("/api/v1/admin/security/health/audit-retention")]
    [InlineData("/api/v1/admin/security/health/audit-integrity")]
    [InlineData("/api/v1/admin/security/health/key-rotation")]
    [InlineData("/api/v1/admin/security/health/backup-history")]
    public async Task AllEndpoints_RequireSecurityHealthViewPermission(string endpoint) =>
        (await CreateRegularUserClient().GetAsync(endpoint))
        .StatusCode.Should().Be(HttpStatusCode.Forbidden);
}
