using AqtCoreFW.Audit.Domain.Services;
using AqtCoreFW.AuthServer.Services;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using FluentAssertions;

namespace AqtCoreFW.AuthServer.Tests.Services;

/// <summary>
/// Tests for JitProvisioningService.
/// Covers all JIT provisioning scenarios: existing links, new provisions, validation checks.
/// Security validation: RT-01 (no email auto-link), RT-02 (domain whitelist), RT-03 (tenant required).
/// </summary>
public sealed class JitProvisioningServiceTests
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IExternalIdentityRepository _externalIdentityRepo;
    private readonly IJitProviderConfigRepository _jitConfigRepo;
    private readonly IAuditService _auditService;
    private readonly IdentityDbContext _dbContext;
    private readonly ILogger<JitProvisioningService> _logger;
    private readonly JitProvisioningService _sut;

    private static readonly Guid TestUserId = Guid.NewGuid();
    private static readonly Guid TestTenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    public JitProvisioningServiceTests()
    {
        var store = Substitute.For<IUserStore<ApplicationUser>>();
        _userManager = Substitute.For<UserManager<ApplicationUser>>(
            store,
            Substitute.For<IOptions<IdentityOptions>>(),
            Substitute.For<IPasswordHasher<ApplicationUser>>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            Substitute.For<ILookupNormalizer>(),
            Substitute.For<IdentityErrorDescriber>(),
            Substitute.For<IServiceProvider>(),
            Substitute.For<ILogger<UserManager<ApplicationUser>>>());

        _externalIdentityRepo = Substitute.For<IExternalIdentityRepository>();
        _jitConfigRepo = Substitute.For<IJitProviderConfigRepository>();
        _auditService = Substitute.For<IAuditService>();
        _logger = Substitute.For<ILogger<JitProvisioningService>>();

        // In-memory DbContext — ConfigureWarnings ignores transaction not-supported
        var dbOptions = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _dbContext = new IdentityDbContext(dbOptions);

        _sut = new JitProvisioningService(
            _userManager, _externalIdentityRepo, _jitConfigRepo, _auditService, _dbContext, _logger);
    }

    #region Existing Link Tests

    [Fact]
    public async Task ExistingLink_Active_ReturnsSuccess()
    {
        // Arrange
        var existingLink = ExternalIdentity.Create(
            TestUserId, ExternalIdentityProvider.SSO, "ext-id-123", "John Doe", "john@test.vn", ActorId);
        var linkedUser = new ApplicationUser
        {
            Id = TestUserId,
            Email = "john@test.vn",
            IsActive = true,
            FullName = "John Doe"
        };

        _externalIdentityRepo
            .GetByProviderAndExternalIdAsync(ExternalIdentityProvider.SSO, "ext-id-123", Arg.Any<CancellationToken>())
            .Returns(existingLink);
        _userManager.FindByIdAsync(TestUserId.ToString()).Returns(linkedUser);
        _externalIdentityRepo.UpdateAsync(Arg.Any<ExternalIdentity>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        // Act
        var result = await _sut.ProvisionOrLinkAsync("SSO", "ext-id-123", "john@test.vn", "John Doe");

        // Assert
        result.Success.Should().BeTrue();
        result.User.Should().NotBeNull();
        result.User?.Id.Should().Be(TestUserId);
        result.ErrorCode.Should().BeNull();
        await _externalIdentityRepo.Received(1).UpdateAsync(Arg.Any<ExternalIdentity>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExistingLink_Inactive_ReturnsDeactivated()
    {
        // Arrange
        var inactiveLink = ExternalIdentity.Create(
            TestUserId, ExternalIdentityProvider.SSO, "ext-id-123", "John Doe", "john@test.vn", ActorId);
        // Simulate inactive by accessing private field via reflection or creating fresh entity and calling Deactivate
        var inactiveLinkProp = inactiveLink.GetType().GetProperty("IsActive",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Public);
        inactiveLinkProp?.SetValue(inactiveLink, false);

        _externalIdentityRepo
            .GetByProviderAndExternalIdAsync(ExternalIdentityProvider.SSO, "ext-id-123", Arg.Any<CancellationToken>())
            .Returns(inactiveLink);

        // Act
        var result = await _sut.ProvisionOrLinkAsync("SSO", "ext-id-123", "john@test.vn", "John Doe");

        // Assert
        result.Success.Should().BeFalse();
        result.User.Should().BeNull();
        result.ErrorCode.Should().Be("deactivated");
    }

    [Fact]
    public async Task ExistingLink_UserInactive_ReturnsDeactivated()
    {
        // Arrange
        var existingLink = ExternalIdentity.Create(
            TestUserId, ExternalIdentityProvider.SSO, "ext-id-123", "John Doe", "john@test.vn", ActorId);
        var inactiveUser = new ApplicationUser
        {
            Id = TestUserId,
            Email = "john@test.vn",
            IsActive = false,
            FullName = "John Doe"
        };

        _externalIdentityRepo
            .GetByProviderAndExternalIdAsync(ExternalIdentityProvider.SSO, "ext-id-123", Arg.Any<CancellationToken>())
            .Returns(existingLink);
        _userManager.FindByIdAsync(TestUserId.ToString()).Returns(inactiveUser);

        // Act
        var result = await _sut.ProvisionOrLinkAsync("SSO", "ext-id-123", "john@test.vn", "John Doe");

        // Assert
        result.Success.Should().BeFalse();
        result.User.Should().BeNull();
        result.ErrorCode.Should().Be("deactivated");
    }

    #endregion

    #region JIT Config Validation Tests

    [Fact]
    public async Task NoLink_JitDisabled_ReturnsJitDisabled()
    {
        // Arrange
        _externalIdentityRepo
            .GetByProviderAndExternalIdAsync(Arg.Any<ExternalIdentityProvider>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ExternalIdentity?)null);

        var disabledConfig = JitProviderConfig.Create(
            "SSO", "SSO Provider", ExternalIdentityProvider.SSO,
            false, "Viewer", false, null, TestTenantId, null, 0, ActorId);

        _jitConfigRepo.GetBySchemeAsync("SSO", Arg.Any<CancellationToken>()).Returns(disabledConfig);

        // Act
        var result = await _sut.ProvisionOrLinkAsync("SSO", "ext-id-456", "jane@test.vn", "Jane Doe");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("jit_disabled");
    }

    [Fact]
    public async Task NoLink_NoTenant_ReturnsNoTenant()
    {
        // Arrange
        _externalIdentityRepo
            .GetByProviderAndExternalIdAsync(Arg.Any<ExternalIdentityProvider>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ExternalIdentity?)null);

        var configNoTenant = JitProviderConfig.Create(
            "SSO", "SSO Provider", ExternalIdentityProvider.SSO,
            true, "Viewer", false, null, null, null, 0, ActorId);

        _jitConfigRepo.GetBySchemeAsync("SSO", Arg.Any<CancellationToken>()).Returns(configNoTenant);

        // Act
        var result = await _sut.ProvisionOrLinkAsync("SSO", "ext-id-456", "jane@test.vn", "Jane Doe");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("no_tenant");
    }

    [Fact]
    public async Task NoLink_DomainNotAllowed_ReturnsDomainNotAllowed()
    {
        // Arrange
        _externalIdentityRepo
            .GetByProviderAndExternalIdAsync(Arg.Any<ExternalIdentityProvider>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ExternalIdentity?)null);

        // Domain whitelist: only @company.vn allowed
        var allowedDomains = System.Text.Json.JsonSerializer.Serialize(new[] { "company.vn" });
        var config = JitProviderConfig.Create(
            "SSO", "SSO Provider", ExternalIdentityProvider.SSO,
            true, "Viewer", false, null, TestTenantId, allowedDomains, 0, ActorId);

        _jitConfigRepo.GetBySchemeAsync("SSO", Arg.Any<CancellationToken>()).Returns(config);

        // Act
        var result = await _sut.ProvisionOrLinkAsync("SSO", "ext-id-456", "jane@gmail.com", "Jane Doe");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("domain_not_allowed");
    }

    #endregion

    #region Email Match & Approval Tests

    [Fact]
    public async Task NoLink_EmailMatch_ReturnsPendingApproval()
    {
        // Arrange: RT-01 — existing user with same email should trigger pending approval
        _externalIdentityRepo
            .GetByProviderAndExternalIdAsync(Arg.Any<ExternalIdentityProvider>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ExternalIdentity?)null);

        var config = JitProviderConfig.Create(
            "SSO", "SSO Provider", ExternalIdentityProvider.SSO,
            true, "Viewer", false, null, TestTenantId, null, 0, ActorId);

        _jitConfigRepo.GetBySchemeAsync("SSO", Arg.Any<CancellationToken>()).Returns(config);

        // Existing user with same email
        var existingUser = new ApplicationUser { Id = TestUserId, Email = "john@test.vn", IsActive = true };
        _userManager.FindByEmailAsync("john@test.vn").Returns(existingUser);

        _auditService.LogLoginAttemptAsync(Arg.Any<Guid?>(), Arg.Any<string>(), "",
            Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.ProvisionOrLinkAsync("SSO", "ext-id-789", "john@test.vn", "John Doe");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("pending_approval");
        await _auditService.Received(1).LogLoginAttemptAsync(
            existingUser.Id, "john@test.vn", "JIT", Arg.Any<string>(), false,
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoLink_NoMatch_CreatesNewUser()
    {
        // Arrange
        _externalIdentityRepo
            .GetByProviderAndExternalIdAsync(Arg.Any<ExternalIdentityProvider>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ExternalIdentity?)null);

        var config = JitProviderConfig.Create(
            "SSO", "SSO Provider", ExternalIdentityProvider.SSO,
            true, "Viewer", false, null, TestTenantId, null, 0, ActorId);

        _jitConfigRepo.GetBySchemeAsync("SSO", Arg.Any<CancellationToken>()).Returns(config);

        // No existing user
        _userManager.FindByEmailAsync(Arg.Any<string>()).Returns((ApplicationUser?)null);
        _userManager.CreateAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);
        _userManager.AddToRoleAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>()).Returns(IdentityResult.Success);
        _externalIdentityRepo.AddAsync(Arg.Any<ExternalIdentity>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        _auditService.LogLoginAttemptAsync(Arg.Any<Guid?>(), Arg.Any<string>(), "",
            Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.ProvisionOrLinkAsync("SSO", "ext-id-new", "newuser@test.vn", "New User");

        // Assert
        result.Success.Should().BeTrue();
        result.User.Should().NotBeNull();
        result.User?.Email.Should().Be("newuser@test.vn");
        result.User?.FullName.Should().Be("New User");
        result.ErrorCode.Should().BeNull();

        await _userManager.Received(1).CreateAsync(Arg.Is<ApplicationUser>(u => u.Email == "newuser@test.vn"));
        await _userManager.Received(1).AddToRoleAsync(Arg.Any<ApplicationUser>(), "Viewer");
        await _externalIdentityRepo.Received(1).AddAsync(Arg.Any<ExternalIdentity>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoLink_RequireApproval_ReturnsPendingApproval()
    {
        // Arrange
        _externalIdentityRepo
            .GetByProviderAndExternalIdAsync(Arg.Any<ExternalIdentityProvider>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ExternalIdentity?)null);

        // Config requires approval
        var config = JitProviderConfig.Create(
            "SSO", "SSO Provider", ExternalIdentityProvider.SSO,
            true, "Viewer", true, null, TestTenantId, null, 0, ActorId); // RequireApproval = true

        _jitConfigRepo.GetBySchemeAsync("SSO", Arg.Any<CancellationToken>()).Returns(config);

        _userManager.FindByEmailAsync(Arg.Any<string>()).Returns((ApplicationUser?)null);
        _userManager.CreateAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);
        _userManager.AddToRoleAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>()).Returns(IdentityResult.Success);
        _externalIdentityRepo.AddAsync(Arg.Any<ExternalIdentity>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        _auditService.LogLoginAttemptAsync(Arg.Any<Guid?>(), Arg.Any<string>(), "",
            Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.ProvisionOrLinkAsync("SSO", "ext-id-pending", "user@test.vn", "Test User");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("pending_approval");
        result.User.Should().BeNull(); // Not returned since pending

        // User created but inactive
        await _userManager.Received(1).CreateAsync(Arg.Is<ApplicationUser>(u => !u.IsActive));
    }

    [Fact]
    public async Task NoLink_CreateFails_ReturnsProvisionFailed()
    {
        // Arrange
        _externalIdentityRepo
            .GetByProviderAndExternalIdAsync(Arg.Any<ExternalIdentityProvider>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ExternalIdentity?)null);

        var config = JitProviderConfig.Create(
            "SSO", "SSO Provider", ExternalIdentityProvider.SSO,
            true, "Viewer", false, null, TestTenantId, null, 0, ActorId);

        _jitConfigRepo.GetBySchemeAsync("SSO", Arg.Any<CancellationToken>()).Returns(config);
        _userManager.FindByEmailAsync(Arg.Any<string>()).Returns((ApplicationUser?)null);

        // Create fails
        _userManager.CreateAsync(Arg.Any<ApplicationUser>())
            .Returns(IdentityResult.Failed(new IdentityError { Description = "User creation failed" }));

        // Act
        var result = await _sut.ProvisionOrLinkAsync("SSO", "ext-id-fail", "fail@test.vn", "Fail User");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("provision_failed");
        result.User.Should().BeNull();
    }

    #endregion

    #region Provider Mapping Tests

    [Fact]
    public async Task VNeIdProvider_MapsCorrectly()
    {
        // Arrange
        _externalIdentityRepo
            .GetByProviderAndExternalIdAsync(ExternalIdentityProvider.VNeID, "vneid-123", Arg.Any<CancellationToken>())
            .Returns((ExternalIdentity?)null);

        var config = JitProviderConfig.Create(
            "VNeID", "VNeID Provider", ExternalIdentityProvider.VNeID,
            true, "Viewer", false, null, TestTenantId, null, 0, ActorId);

        _jitConfigRepo.GetBySchemeAsync("VNeID", Arg.Any<CancellationToken>()).Returns(config);
        _userManager.FindByEmailAsync(Arg.Any<string>()).Returns((ApplicationUser?)null);
        _userManager.CreateAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);
        _userManager.AddToRoleAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>()).Returns(IdentityResult.Success);
        _externalIdentityRepo.AddAsync(Arg.Any<ExternalIdentity>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        _auditService.LogLoginAttemptAsync(Arg.Any<Guid?>(), Arg.Any<string>(), "",
            Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.ProvisionOrLinkAsync("VNeID", "vneid-123", "user@test.vn", "VN User");

        // Assert
        result.Success.Should().BeTrue();
        result.User?.AuthSource.Should().Be("VNEID");
    }

    [Fact]
    public async Task LdapProvider_MapsCorrectly()
    {
        // Arrange
        _externalIdentityRepo
            .GetByProviderAndExternalIdAsync(ExternalIdentityProvider.LDAP, "ldap-dn", Arg.Any<CancellationToken>())
            .Returns((ExternalIdentity?)null);

        var config = JitProviderConfig.Create(
            "LDAP", "LDAP Provider", ExternalIdentityProvider.LDAP,
            true, "Viewer", false, null, TestTenantId, null, 0, ActorId);

        _jitConfigRepo.GetBySchemeAsync("LDAP", Arg.Any<CancellationToken>()).Returns(config);
        _userManager.FindByEmailAsync(Arg.Any<string>()).Returns((ApplicationUser?)null);
        _userManager.CreateAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);
        _userManager.AddToRoleAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>()).Returns(IdentityResult.Success);
        _externalIdentityRepo.AddAsync(Arg.Any<ExternalIdentity>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        _auditService.LogLoginAttemptAsync(Arg.Any<Guid?>(), Arg.Any<string>(), "",
            Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.ProvisionOrLinkAsync("LDAP", "ldap-dn", "user@test.vn", "LDAP User");

        // Assert
        result.Success.Should().BeTrue();
        result.User?.AuthSource.Should().Be("LDAP");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task NoEmail_GeneratesFallbackUsername()
    {
        // Arrange
        _externalIdentityRepo
            .GetByProviderAndExternalIdAsync(Arg.Any<ExternalIdentityProvider>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ExternalIdentity?)null);

        var config = JitProviderConfig.Create(
            "SSO", "SSO Provider", ExternalIdentityProvider.SSO,
            true, "Viewer", false, null, TestTenantId, null, 0, ActorId);

        _jitConfigRepo.GetBySchemeAsync("SSO", Arg.Any<CancellationToken>()).Returns(config);
        _userManager.FindByEmailAsync(Arg.Any<string>()).Returns((ApplicationUser?)null);
        _userManager.CreateAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);
        _userManager.AddToRoleAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>()).Returns(IdentityResult.Success);
        _externalIdentityRepo.AddAsync(Arg.Any<ExternalIdentity>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        _auditService.LogLoginAttemptAsync(Arg.Any<Guid?>(), Arg.Any<string>(), "",
            Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.ProvisionOrLinkAsync("SSO", "ext-id-999", null, "Unknown User");

        // Assert
        result.Success.Should().BeTrue();
        result.User?.UserName.Should().Be("ext-id-999@SSO");
        result.User?.Email.Should().BeNull();
    }

    [Fact]
    public async Task EmptyAllowedDomains_AllowsAllDomains()
    {
        // Arrange: Empty domain list means allow all
        _externalIdentityRepo
            .GetByProviderAndExternalIdAsync(Arg.Any<ExternalIdentityProvider>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ExternalIdentity?)null);

        var allowedDomains = System.Text.Json.JsonSerializer.Serialize(Array.Empty<string>());
        var config = JitProviderConfig.Create(
            "SSO", "SSO Provider", ExternalIdentityProvider.SSO,
            true, "Viewer", false, null, TestTenantId, allowedDomains, 0, ActorId);

        _jitConfigRepo.GetBySchemeAsync("SSO", Arg.Any<CancellationToken>()).Returns(config);
        _userManager.FindByEmailAsync(Arg.Any<string>()).Returns((ApplicationUser?)null);
        _userManager.CreateAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);
        _userManager.AddToRoleAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>()).Returns(IdentityResult.Success);
        _externalIdentityRepo.AddAsync(Arg.Any<ExternalIdentity>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        _auditService.LogLoginAttemptAsync(Arg.Any<Guid?>(), Arg.Any<string>(), "",
            Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.ProvisionOrLinkAsync("SSO", "ext-id-open", "user@any-domain.com", "Any User");

        // Assert
        result.Success.Should().BeTrue();
    }

    #endregion
}
