using AqtCoreFW.Identity.Domain.Models;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Services;
using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using Dapper;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Services;

/// <summary>
/// Unit tests for InProcessIdentityModuleClient.
/// Mocks IReadDbConnection (Dapper) + IEffectivePermissionService + IPepperResolver — no real DB required.
/// Updated for Phase 1 (class-based DTOs, tenant params, hard cap) and Phase-02
/// (GetEffectivePermissionCodesAsync delegation) and Wave-3 (EmailHashService injection).
/// </summary>
public sealed class InProcessIdentityModuleClientTests
{
    private readonly IReadDbConnection _db = Substitute.For<IReadDbConnection>();
    private readonly IEffectivePermissionService _permissionService = Substitute.For<IEffectivePermissionService>();
    private readonly InProcessIdentityModuleClient _sut;

    public InProcessIdentityModuleClientTests()
    {
        // Stub pepper resolver with a deterministic 32-byte pepper so EmailHashService can compute hashes.
        var pepper = new byte[32];
        Array.Fill(pepper, (byte)0xAB);
        var pepperResolver = Substitute.For<IPepperResolver>();
        pepperResolver.GetPepper(Arg.Any<Guid>()).Returns(pepper);

        var emailHashService = new EmailHashService(pepperResolver);
        // Passthrough decryptor — GetUserInfoByIds tests use plaintext mock rows; real decryption is
        // covered by EncryptedFieldDecryptor unit tests and integration tests with the live key resolver.
        var decryptor = Substitute.For<IEncryptedFieldDecryptor>();
        decryptor.Decrypt(Arg.Any<string?>()).Returns(ci => ci.Arg<string?>());
        _sut = new InProcessIdentityModuleClient(_db, _permissionService, emailHashService, decryptor);
    }

    [Fact]
    public async Task GetUserInfoByIdsAsync_EmptyInput_ReturnsEmptyDictionary()
    {
        var result = await _sut.GetUserInfoByIdsAsync([], ct: CancellationToken.None);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUserInfoByIdsAsync_NullInput_ReturnsEmptyDictionary()
    {
        var result = await _sut.GetUserInfoByIdsAsync(null!, ct: CancellationToken.None);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUserInfoByIdsAsync_ExceedsMaxBatchSize_ThrowsArgumentException()
    {
        // [RT-09] Hard cap 5000 IDs
        var tooManyIds = Enumerable.Range(0, 5001).Select(_ => Guid.NewGuid()).ToList();

        var act = () => _sut.GetUserInfoByIdsAsync(tooManyIds, ct: CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("userIds");
    }

    [Fact]
    public async Task FindByEmailAsync_NonExistentUser_ReturnsNull()
    {
        // IReadDbConnection.QueryFirstOrDefaultAsync returns default(T) = null for class
        _db.QueryFirstOrDefaultAsync<UserInfoDto>(
            Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>(), Arg.Any<int?>())
            .Returns((UserInfoDto?)null);

        var result = await _sut.FindByEmailAsync("nobody@gov.vn");
        result.Should().BeNull();
    }

    [Fact]
    public async Task FindByEmailAsync_WithTenantId_IncludesTenantFilter()
    {
        // [RT-05] Verify tenant-scoped lookup passes tenantId in query
        var tenantId = Guid.NewGuid();
        var expected = new UserInfoDto
        {
            UserId = Guid.NewGuid(),
            Email = "test@gov.vn",
            TenantId = tenantId
        };

        _db.QueryFirstOrDefaultAsync<UserInfoDto>(
            Arg.Is<string>(sql => sql.Contains("TenantId")),
            Arg.Any<object?>(), Arg.Any<CancellationToken>(), Arg.Any<int?>())
            .Returns(expected);

        var result = await _sut.FindByEmailAsync("test@gov.vn", tenantId);
        result.Should().NotBeNull();
        result!.TenantId.Should().Be(tenantId);
    }

    // --- Phase-02: GetEffectivePermissionCodesAsync delegation ---

    [Fact]
    public async Task GetEffectivePermissionCodesAsync_DelegatesToPermissionService_ReturnsCodes()
    {
        var userId = Guid.NewGuid();
        var codes = new HashSet<string> { "CASES.WORKFLOW.APPROVE", "CASES.WORKFLOW.SUBMIT" };
        _permissionService.GetSummaryAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new EffectivePermissionSummary { UserId = userId, PermissionCodes = codes });

        var result = await _sut.GetEffectivePermissionCodesAsync(userId);

        result.Should().BeEquivalentTo(codes);
        await _permissionService.Received(1).GetSummaryAsync(userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetEffectivePermissionCodesAsync_UserWithNoPermissions_ReturnsEmptySet()
    {
        var userId = Guid.NewGuid();
        _permissionService.GetSummaryAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new EffectivePermissionSummary { UserId = userId });

        var result = await _sut.GetEffectivePermissionCodesAsync(userId);

        result.Should().BeEmpty();
    }

    // --- T3-WF-CANDIDATEINDEX-READSIDE-EXTENSION Phase 01: GetUserRoleCodesAsync ---
    // PQ-1 A direct-only semantic. Plan: plans/260524-2122-readside-extension-role-only/phase-01.

    [Fact]
    public async Task GetUserRoleCodesAsync_DirectRolesOnly_ReturnsHashSetWithOrdinalComparer()
    {
        // Stub returns post-SQL-canonicalized names (UPPER+TRIM done by SQL).
        // Test asserts C# wraps in HashSet<string> with StringComparer.Ordinal.
        _db.QueryAsync<string>(
            Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>(), Arg.Any<int?>())
            .Returns(new[] { "ADMIN", "REVIEWER", "AUDITOR" });

        var result = await _sut.GetUserRoleCodesAsync(Guid.NewGuid(), Guid.NewGuid());

        result.Should().BeEquivalentTo(["ADMIN", "REVIEWER", "AUDITOR"]);
        // Ordinal comparer: case-sensitive lookup proves comparer choice.
        result.Contains("ADMIN").Should().BeTrue();
        result.Contains("admin").Should().BeFalse();
    }

    [Fact]
    public async Task GetUserRoleCodesAsync_EmptyResult_ReturnsEmptySetNotNull()
    {
        _db.QueryAsync<string>(
            Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>(), Arg.Any<int?>())
            .Returns(Array.Empty<string>());

        var result = await _sut.GetUserRoleCodesAsync(Guid.NewGuid(), Guid.NewGuid());

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUserRoleCodesAsync_PassesUserIdAndTenantIdParameters()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        object? capturedParams = null;

        _db.QueryAsync<string>(
                Arg.Any<string>(),
                Arg.Do<object?>(p => capturedParams = p),
                Arg.Any<CancellationToken>(),
                Arg.Any<int?>())
            .Returns(Array.Empty<string>());

        await _sut.GetUserRoleCodesAsync(userId, tenantId);

        capturedParams.Should().BeOfType<DynamicParameters>();
        var dp = (DynamicParameters)capturedParams!;
        dp.ParameterNames.Should().Contain("UserId");
        dp.ParameterNames.Should().Contain("TenantId");
        dp.Get<Guid>("UserId").Should().Be(userId);
        dp.Get<Guid>("TenantId").Should().Be(tenantId);
    }

    [Fact]
    public async Task GetUserRoleCodesAsync_DoesNotDelegateToPermissionService()
    {
        // Anti-tautology PQ-1 A lock (260522-E): any refactor that proxies through
        // IEffectivePermissionService would silently leak group-inherited roles.
        _db.QueryAsync<string>(
            Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>(), Arg.Any<int?>())
            .Returns(Array.Empty<string>());

        await _sut.GetUserRoleCodesAsync(Guid.NewGuid(), Guid.NewGuid());

        await _permissionService.DidNotReceiveWithAnyArgs()
            .GetSummaryAsync(default, default);
    }

    // --- T3-WF-CANDIDATEINDEX-READSIDE-EXT-GROUP: GetUserGroupCodesAsync ---
    // Mirror of GetUserRoleCodesAsync. Direct-membership only (no group-inheritance equivalent
    // exists for groups themselves, but tested anti-tautology against ResolveActiveUsersInGroups).

    [Fact]
    public async Task GetUserGroupCodesAsync_DirectMembership_ReturnsHashSetWithOrdinalComparer()
    {
        _db.QueryAsync<string>(
            Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>(), Arg.Any<int?>())
            .Returns(new[] { "GROUP_TIEP_NHAN", "GROUP_REVIEW" });

        var result = await _sut.GetUserGroupCodesAsync(Guid.NewGuid(), Guid.NewGuid());

        result.Should().BeEquivalentTo(["GROUP_TIEP_NHAN", "GROUP_REVIEW"]);
        // Ordinal comparer: case-sensitive lookup proves comparer choice (matches Save-time canonicalization).
        result.Contains("GROUP_TIEP_NHAN").Should().BeTrue();
        result.Contains("group_tiep_nhan").Should().BeFalse();
    }

    [Fact]
    public async Task GetUserGroupCodesAsync_EmptyResult_ReturnsEmptySetNotNull()
    {
        _db.QueryAsync<string>(
            Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>(), Arg.Any<int?>())
            .Returns(Array.Empty<string>());

        var result = await _sut.GetUserGroupCodesAsync(Guid.NewGuid(), Guid.NewGuid());

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUserGroupCodesAsync_PassesUserIdAndTenantIdParameters()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        object? capturedParams = null;

        _db.QueryAsync<string>(
                Arg.Any<string>(),
                Arg.Do<object?>(p => capturedParams = p),
                Arg.Any<CancellationToken>(),
                Arg.Any<int?>())
            .Returns(Array.Empty<string>());

        await _sut.GetUserGroupCodesAsync(userId, tenantId);

        capturedParams.Should().BeOfType<DynamicParameters>();
        var dp = (DynamicParameters)capturedParams!;
        dp.ParameterNames.Should().Contain("UserId");
        dp.ParameterNames.Should().Contain("TenantId");
        dp.Get<Guid>("UserId").Should().Be(userId);
        dp.Get<Guid>("TenantId").Should().Be(tenantId);
    }

    [Fact]
    public async Task GetUserGroupCodesAsync_DoesNotDelegateToPermissionService()
    {
        // Anti-tautology lock: any refactor that proxies through IEffectivePermissionService
        // (which merges group-inherited roles) would conflate group membership with role inheritance.
        _db.QueryAsync<string>(
            Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>(), Arg.Any<int?>())
            .Returns(Array.Empty<string>());

        await _sut.GetUserGroupCodesAsync(Guid.NewGuid(), Guid.NewGuid());

        await _permissionService.DidNotReceiveWithAnyArgs()
            .GetSummaryAsync(default, default);
    }

    // --- RT2-15 Phase 02: ResolveActiveUsersInGroupsAsync (runtime resolver) ---

    [Fact]
    public async Task ResolveActiveUsersInGroupsAsync_EmptyCodes_ReturnsEmpty()
    {
        // No DB call expected — early return on empty input.
        var result = await _sut.ResolveActiveUsersInGroupsAsync([], Guid.NewGuid(), CancellationToken.None);

        result.Should().BeEmpty();
        await _db.DidNotReceiveWithAnyArgs()
            .QueryAsync<Guid>(default!, default, default, default);
    }

    [Fact]
    public async Task ResolveActiveUsersInGroupsAsync_FiltersInactiveGroupAndInactiveUser_ReturnsActiveOnly()
    {
        // SQL WHERE g.IsActive=1 AND u.IsActive=1 AND (g.TenantId=@TenantId OR g.TenantId IS NULL) applied server-side.
        // Stub returns only the IDs SQL would produce after filtering.
        var expectedId = Guid.NewGuid();
        _db.QueryAsync<Guid>(
            Arg.Is<string>(sql => sql.Contains("UserGroupMemberships") && sql.Contains("IsActive") && sql.Contains("TenantId")),
            Arg.Any<object?>(), Arg.Any<CancellationToken>(), Arg.Any<int?>())
            .Returns(new[] { expectedId });

        var result = await _sut.ResolveActiveUsersInGroupsAsync(
            ["GROUP_A", "GROUP_B"], Guid.NewGuid(), CancellationToken.None);

        result.Should().BeEquivalentTo(new[] { expectedId });
    }

    // --- RT2-15 Phase 02: ResolveActiveUserGroupsByCodesAsync (Save-validation Q2) ---

    [Fact]
    public async Task ResolveActiveUserGroupsByCodesAsync_EmptyCodes_ReturnsEmpty()
    {
        // No DB call expected — early return on empty input.
        var result = await _sut.ResolveActiveUserGroupsByCodesAsync([], Guid.NewGuid(), CancellationToken.None);

        result.Should().BeEmpty();
        await _db.DidNotReceiveWithAnyArgs()
            .QueryAsync<string>(default!, default, default, default);
    }

    [Fact]
    public async Task ResolveActiveUserGroupsByCodesAsync_KnownAndUnknownMix_ReturnsOnlyKnown()
    {
        // Anti-tautology (260522-E): input has 2 codes; SQL returns only the active known one.
        // Caller computes set-difference to detect unknown; this test locks the SUBSET-only guarantee.
        // SQL now also filters by TenantId — assert both IsActive and TenantId appear in query.
        _db.QueryAsync<string>(
            Arg.Is<string>(sql => sql.Contains("UserGroups") && sql.Contains("IsActive") && sql.Contains("TenantId")),
            Arg.Any<object?>(), Arg.Any<CancellationToken>(), Arg.Any<int?>())
            .Returns(new[] { "GROUP_KNOWN" });

        var result = await _sut.ResolveActiveUserGroupsByCodesAsync(
            ["GROUP_KNOWN", "GROUP_TYPO"], Guid.NewGuid(), CancellationToken.None);

        result.Should().BeEquivalentTo(new[] { "GROUP_KNOWN" },
            "only codes that resolved to an active UserGroup must be returned");
        result.Should().NotContain("GROUP_TYPO",
            "unknown/inactive code must NOT appear in the returned set (caller detects via set-difference)");
        // Ordinal comparer lock: GROUP_KNOWN must be findable by exact case.
        result.Contains("GROUP_KNOWN").Should().BeTrue();
        result.Contains("group_known").Should().BeFalse();
    }
}
