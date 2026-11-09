using Xunit;

namespace AqtCoreFW.Tests.Integration.Infrastructure;

/// <summary>
/// Base class for all integration tests.
/// Injects DatabaseFixture via xUnit collection fixture — shared across all test classes.
/// Factory is accessed via db.Factory (set after migrations + seeders complete in InitializeAsync).
/// Default Client is pre-authenticated as SystemAdmin.
/// </summary>
[Collection("Integration")]
public abstract class IntegrationTestBase(DatabaseFixture db) : IAsyncLifetime
{
    protected ApiFactory Factory { get; } = db.Factory;

    /// <summary>Pre-authenticated HttpClient (SystemAdmin role). Set in InitializeAsync.</summary>
    protected HttpClient Client { get; private set; } = null!;

    /// <summary>
    /// Creates an authenticated HttpClient with the specified identity.
    /// Caller is responsible for disposing.
    /// </summary>
    protected HttpClient CreateAuthenticatedClient(
        string? userId = null,
        string[]? roles = null,
        string? tenantId = null) =>
        Factory.CreateAuthenticatedClient(userId, roles, tenantId);

    /// <summary>
    /// Creates an HttpClient with explicit permission codes for fine-grained RBAC tests.
    /// Use this when testing specific permission combinations (e.g., View but not Manage).
    /// Caller is responsible for disposing.
    /// </summary>
    protected HttpClient CreateClientWithPermissions(
        string[] permissions,
        string? userId = null,
        string? tenantId = null) =>
        Factory.CreateClientWithPermissions(permissions, userId, tenantId);

    // Fixed tenant id used for the default SystemAdmin client.
    // Must be provided because controllers resolve tenant from JWT claims (F-01 rule)
    // and return 403 when tenantContext.TenantId is null.
    protected static readonly Guid DefaultTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public virtual Task InitializeAsync()
    {
        // SystemAdmin is the widest role — individual tests can narrow via CreateAuthenticatedClient.
        // A tenantId is required because FilesController and others resolve tenant from JWT (not
        // from query params) per the F-01 security rule; without it they return 403 Forbidden.
        Client = CreateAuthenticatedClient(roles: ["SystemAdmin"], tenantId: DefaultTenantId.ToString());
        return Task.CompletedTask;
    }

    public virtual Task DisposeAsync()
    {
        Client.Dispose();
        return Task.CompletedTask;
    }
}
