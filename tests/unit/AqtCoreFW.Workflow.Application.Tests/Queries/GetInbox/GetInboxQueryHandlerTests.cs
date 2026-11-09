using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Queries.GetInbox;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Errors;
using FluentAssertions;
using FluentResults;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Workflow.Application.Tests.Queries.GetInbox;

/// <summary>
/// Unit tests for GetInboxQueryHandler — Phase 4-C.
///
/// F6 (primary): NotImplementedMode guard — any non-null, non-"ByPermission" ResolverMode
///   returns ValidationError with GOV_WFL_018 in BOTH ErrorCode and WorkflowErrorCode metadata.
///   Anti-tautology: asserts GOV_WFL_018 literally appears, not generic GOV_SYS_000 fallback.
///   4-B-A INVARIANT verified: both metadata keys set.
///
/// Additional facts covering happy path and identity-client interaction.
/// RT2-15 Phase 04: handler now accepts IOrgUnitModuleClient (3rd ctor param).
///   Tests stub orgUnitClient to return empty set (non-OrgUnit tests unaffected).
/// </summary>
public sealed class GetInboxQueryHandlerTests
{
    private readonly IWorkflowTaskRepository _repository;
    private readonly IIdentityModuleClient _identityClient;
    private readonly IOrgUnitModuleClient _orgUnitClient;
    private readonly GetInboxQueryHandler _handler;

    private static readonly Guid TestUserId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TestTenantId = new("22222222-2222-2222-2222-222222222222");
    private static readonly IReadOnlySet<string> EmptyPermissions = new HashSet<string>();
    private static readonly IReadOnlySet<string> EmptyRoles = new HashSet<string>();
    private static readonly IReadOnlySet<string> EmptyGroups = new HashSet<string>();
    private static readonly IReadOnlySet<string> EmptyOrgUnits = new HashSet<string>();

    public GetInboxQueryHandlerTests()
    {
        _repository = Substitute.For<IWorkflowTaskRepository>();
        _identityClient = Substitute.For<IIdentityModuleClient>();
        _orgUnitClient = Substitute.For<IOrgUnitModuleClient>();
        _handler = new GetInboxQueryHandler(_repository, _identityClient, _orgUnitClient);

        // Default: identity client returns empty permissions + empty roles + empty groups;
        // orgUnitClient returns empty org unit codes; repo returns empty list.
        _identityClient.GetEffectivePermissionCodesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(EmptyPermissions);
        _identityClient.GetUserRoleCodesAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(EmptyRoles);
        _identityClient.GetUserGroupCodesAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(EmptyGroups);
        _orgUnitClient.GetUserOrgUnitCodesAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(EmptyOrgUnits);
        _repository.GetInboxAsync(
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlySet<string>>(),
                Arg.Any<IReadOnlySet<string>>(),
                Arg.Any<IReadOnlySet<string>>(),
                Arg.Any<IReadOnlySet<string>>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WorkflowTaskDto>>([]));
    }

    // ─────────────────────────────────────────────────────────────────────
    // F6: NotImplementedMode guard
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// F6a: Non-null, non-"ByPermission" ResolverMode → ValidationError with GOV_WFL_018.
    /// Anti-tautology (4-B-A): asserts "GOV_WFL_018" literal in BOTH metadata keys,
    /// NOT the generic "GOV_SYS_000" fallback that fires when ErrorCode metadata is absent.
    /// </summary>
    [Theory]
    [InlineData("ByRole")]
    [InlineData("ByGroup")]
    [InlineData("ByOrgUnit")]
    [InlineData("Unknown")]
    public async Task Handle_UnsupportedResolverMode_ReturnsNotImplementedModeError(string mode)
    {
        var query = new GetInboxQuery(TestUserId, TestTenantId, ResolverMode: mode);

        var result = await _handler.Handle(query, CancellationToken.None);

        // Must fail
        result.IsFailed.Should().BeTrue(
            $"mode '{mode}' is not implemented — handler must return failure");

        var error = result.Errors.First();

        // 4-B-A INVARIANT: both metadata keys must be set.
        error.Metadata.Should().ContainKey("ErrorCode",
            "4-B-A INVARIANT: ErrorCode metadata is required for ApiResponse.Fail body 'code' field");
        error.Metadata["ErrorCode"].Should().Be(WorkflowErrorCodes.NotImplementedMode,
            $"ErrorCode must be GOV_WFL_018 (NotImplementedMode), not generic fallback GOV_SYS_000");

        error.Metadata.Should().ContainKey("WorkflowErrorCode",
            "4-B-A INVARIANT: WorkflowErrorCode metadata required for cross-module provenance");
        error.Metadata["WorkflowErrorCode"].Should().Be(WorkflowErrorCodes.NotImplementedMode,
            "WorkflowErrorCode must also be GOV_WFL_018");

        // Error message must surface the unsupported mode name for caller context.
        error.Message.Should().Contain(mode,
            "error message must include the requested mode so caller can identify which mode failed");

        // Identity client must NOT be called — guard fires before any I/O.
        await _identityClient.DidNotReceive().GetEffectivePermissionCodesAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// F6b: Explicit "ByPermission" ResolverMode (case-insensitive) succeeds — not rejected.
    /// Proves the guard is not over-eager.
    /// </summary>
    [Theory]
    [InlineData("ByPermission")]
    [InlineData("bypermission")]
    [InlineData("BYPERMISSION")]
    public async Task Handle_ByPermissionModeExplicit_CaseInsensitive_Succeeds(string mode)
    {
        var query = new GetInboxQuery(TestUserId, TestTenantId, ResolverMode: mode);

        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue(
            $"explicit '{mode}' mode must not be rejected — it is the only implemented mode");
    }

    /// <summary>
    /// F6c: Null ResolverMode (default) succeeds — null = ByPermission (MVP default).
    /// </summary>
    [Fact]
    public async Task Handle_NullResolverMode_DefaultsByPermission_Succeeds()
    {
        var query = new GetInboxQuery(TestUserId, TestTenantId); // ResolverMode defaults to null

        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue("null ResolverMode = default ByPermission mode, must succeed");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Happy path: identity client + repository interactions
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Handler calls identity client with the caller's UserId, then passes returned codes to repo.
    /// Proves the permission-code propagation thread.
    /// </summary>
    [Fact]
    public async Task Handle_CallsIdentityClientWithUserId_AndPassesCodestoRepo()
    {
        var permCodes = new HashSet<string> { "Workflow.Task.Process" };
        _identityClient.GetEffectivePermissionCodesAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(permCodes);

        var query = new GetInboxQuery(TestUserId, TestTenantId);
        await _handler.Handle(query, CancellationToken.None);

        // Identity client called with correct UserId.
        await _identityClient.Received(1).GetEffectivePermissionCodesAsync(
            TestUserId, Arg.Any<CancellationToken>());

        // Repo called with the exact permission codes returned by identity client.
        await _repository.Received(1).GetInboxAsync(
            TestUserId,
            Arg.Is<IReadOnlySet<string>>(codes => codes.SetEquals(permCodes)),
            Arg.Any<IReadOnlySet<string>>(),
            Arg.Any<IReadOnlySet<string>>(),
            Arg.Any<IReadOnlySet<string>>(),
            TestTenantId,
            Arg.Any<CancellationToken>());
    }

    // ─────────────────────────────────────────────────────────────────────
    // T3-WF-CANDIDATEINDEX-READSIDE-EXTENSION Phase 02: roleNames propagation
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// F-Role-1: Handler fetches role names from identity client with UserId + TenantId.
    /// Locks the pipeline GetInboxQueryHandler → IIdentityModuleClient.GetUserRoleCodesAsync.
    /// </summary>
    [Fact]
    public async Task Handle_FetchesRoleNamesFromIdentityClient_WithUserIdAndTenantId()
    {
        var query = new GetInboxQuery(TestUserId, TestTenantId);

        await _handler.Handle(query, CancellationToken.None);

        await _identityClient.Received(1).GetUserRoleCodesAsync(
            TestUserId, TestTenantId, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// F-Role-2: Role names returned by identity client are passed verbatim to the repository.
    /// Locks the pipeline IIdentityModuleClient → IWorkflowTaskRepository.GetInboxAsync(roleNames).
    /// </summary>
    [Fact]
    public async Task Handle_PassesRoleNamesToRepository()
    {
        var roleNames = new HashSet<string>(StringComparer.Ordinal) { "REVIEWER", "APPROVER" };
        _identityClient.GetUserRoleCodesAsync(
                TestUserId, TestTenantId, Arg.Any<CancellationToken>())
            .Returns(roleNames);

        var query = new GetInboxQuery(TestUserId, TestTenantId);
        await _handler.Handle(query, CancellationToken.None);

        await _repository.Received(1).GetInboxAsync(
            TestUserId,
            Arg.Any<IReadOnlySet<string>>(),
            Arg.Is<IReadOnlySet<string>>(roles => roles.SetEquals(roleNames)),
            Arg.Any<IReadOnlySet<string>>(),
            Arg.Any<IReadOnlySet<string>>(),
            TestTenantId,
            Arg.Any<CancellationToken>());
    }

    // ─────────────────────────────────────────────────────────────────────
    // T3-WF-CANDIDATEINDEX-READSIDE-EXT-GROUP: groupCodes propagation
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// F-Group-1: Handler fetches group codes from identity client with UserId + TenantId.
    /// Locks the pipeline GetInboxQueryHandler → IIdentityModuleClient.GetUserGroupCodesAsync.
    /// </summary>
    [Fact]
    public async Task Handle_FetchesGroupCodesFromIdentityClient_WithUserIdAndTenantId()
    {
        var query = new GetInboxQuery(TestUserId, TestTenantId);

        await _handler.Handle(query, CancellationToken.None);

        await _identityClient.Received(1).GetUserGroupCodesAsync(
            TestUserId, TestTenantId, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// F-Group-2: Group codes returned by identity client are passed verbatim to the repository.
    /// Locks the pipeline IIdentityModuleClient → IWorkflowTaskRepository.GetInboxAsync(groupCodes).
    /// </summary>
    [Fact]
    public async Task Handle_PassesGroupCodesToRepository()
    {
        var groupCodes = new HashSet<string>(StringComparer.Ordinal) { "GROUP_TIEP_NHAN", "GROUP_REVIEW" };
        _identityClient.GetUserGroupCodesAsync(
                TestUserId, TestTenantId, Arg.Any<CancellationToken>())
            .Returns(groupCodes);

        var query = new GetInboxQuery(TestUserId, TestTenantId);
        await _handler.Handle(query, CancellationToken.None);

        await _repository.Received(1).GetInboxAsync(
            TestUserId,
            Arg.Any<IReadOnlySet<string>>(),
            Arg.Any<IReadOnlySet<string>>(),
            Arg.Is<IReadOnlySet<string>>(groups => groups.SetEquals(groupCodes)),
            Arg.Any<IReadOnlySet<string>>(),
            TestTenantId,
            Arg.Any<CancellationToken>());
    }

    // ─────────────────────────────────────────────────────────────────────
    // RT2-15 Phase 04: orgUnitCodes propagation
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// F-OrgUnit-1: Handler fetches org unit codes from orgUnitClient with UserId + TenantId.
    /// Locks the pipeline GetInboxQueryHandler → IOrgUnitModuleClient.GetUserOrgUnitCodesAsync.
    /// </summary>
    [Fact]
    public async Task Handle_FetchesOrgUnitCodesFromOrgUnitClient_WithUserIdAndTenantId()
    {
        var query = new GetInboxQuery(TestUserId, TestTenantId);

        await _handler.Handle(query, CancellationToken.None);

        await _orgUnitClient.Received(1).GetUserOrgUnitCodesAsync(
            TestUserId, TestTenantId, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// F-OrgUnit-2: Org unit codes returned by orgUnitClient are passed verbatim to the repository.
    /// Locks the pipeline IOrgUnitModuleClient → IWorkflowTaskRepository.GetInboxAsync(orgUnitCodes).
    /// </summary>
    [Fact]
    public async Task Handle_PassesOrgUnitCodesToRepository()
    {
        var orgUnitCodes = new HashSet<string>(StringComparer.Ordinal) { "OU_PHONG_TIEP_NHAN", "OU_BAN_GIAM_DOC" };
        _orgUnitClient.GetUserOrgUnitCodesAsync(
                TestUserId, TestTenantId, Arg.Any<CancellationToken>())
            .Returns(orgUnitCodes);

        var query = new GetInboxQuery(TestUserId, TestTenantId);
        await _handler.Handle(query, CancellationToken.None);

        await _repository.Received(1).GetInboxAsync(
            TestUserId,
            Arg.Any<IReadOnlySet<string>>(),
            Arg.Any<IReadOnlySet<string>>(),
            Arg.Any<IReadOnlySet<string>>(),
            Arg.Is<IReadOnlySet<string>>(codes => codes.SetEquals(orgUnitCodes)),
            TestTenantId,
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Handler returns the list from repository as-is (no re-mapping in handler).
    /// </summary>
    [Fact]
    public async Task Handle_ReturnsRepoResultDirectly()
    {
        var dto = new WorkflowTaskDto(
            Id: 1L,
            WorkflowInstanceId: Guid.NewGuid(),
            Title: "Test Task",
            Description: null,
            AssigneeId: TestUserId,
            Status: AqtCoreFW.Workflow.Domain.Entities.WorkflowTaskStatus.Open,
            Priority: AqtCoreFW.Workflow.Domain.Entities.WorkflowTaskPriority.Normal,
            DueDate: null,
            CompletedAt: null,
            CompletedBy: null,
            TenantId: TestTenantId,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedBy: Guid.NewGuid());

        _repository.GetInboxAsync(
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlySet<string>>(),
                Arg.Any<IReadOnlySet<string>>(),
                Arg.Any<IReadOnlySet<string>>(),
                Arg.Any<IReadOnlySet<string>>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WorkflowTaskDto>>([dto]));

        var query = new GetInboxQuery(TestUserId, TestTenantId);
        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].Id.Should().Be(1L);
        result.Value[0].Title.Should().Be("Test Task");
    }
}
