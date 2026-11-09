using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Application.Validation;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Domain.Validation;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Validation;

/// <summary>
/// Unit tests for the RB5 live-claimant check on <see cref="WorkflowGraphValidator.ValidateForActivateAsync"/>.
/// This is the ONLY layer where the SUCCESS branch is provable: integration tests authenticate synthetic
/// users that have no real <c>AspNetUserRoles</c> rows, so a "held role" can't be seeded there — only a
/// mocked <see cref="IIdentityModuleClient.ResolveActiveUsersInRolesAsync"/> can return a claimant.
///
/// The graph is intentionally minimal (no states/transitions) so only the branch rules fire; assertions
/// target the claimant-specific RB5 detail ("no active user") to isolate it from structural RB5/R1 noise.
/// </summary>
public sealed class WorkflowGraphValidatorBranchClaimantRulesTests
{
    private const long DefId = 1L;
    private const string ClaimantViolationMarker = "no active user";

    private static IIdentityModuleClient IdentityResolvingTo(params Guid[] users)
    {
        var client = Substitute.For<IIdentityModuleClient>();
        client.ResolveActiveUsersInRolesAsync(
                Arg.Any<IReadOnlyList<string>>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Guid>>(users));
        return client;
    }

    private static Task<WorkflowGraphValidationReport> ValidateActivate(
        IIdentityModuleClient identity, WorkflowParallelBranch branch)
    {
        var validator = new WorkflowGraphValidator(identity);
        return validator.ValidateForActivateAsync(
            states: [],
            transitions: [],
            assignmentByTransitionId: new Dictionary<long, OutgoingAssignment?>(),
            tenantId: Guid.NewGuid(),
            branches: [branch],
            ct: CancellationToken.None);
    }

    // Single branch (Submitted → {child}) with one child carrying the given pool clause.
    private static WorkflowParallelBranch BranchWithChildPool(AssignmentClauseMode mode, string refsJson)
    {
        var branch = WorkflowParallelBranch.Create(
            DefId, branchingStateId: 1, continuationStateId: 4,
            ParallelBranchType.And, requiredCount: null, allowCancelOnAnyReject: false, timeoutMinutes: 0);

        var child = WorkflowBranchChild.Create(
            parallelBranchId: 0, targetStateId: 2, name: "review-b", displayNameVi: "Duyệt B",
            departmentId: null, sortOrder: 1);

        var assignment = WorkflowBranchChildAssignment.Create(branchChildId: 0);
        assignment.AddClause(mode, refsJson);
        child.SetAssignment(assignment);

        branch.AddChild(child);
        return branch;
    }

    [Fact]
    public async Task ByRolePool_WithActiveClaimant_NoClaimantViolation()
    {
        var identity = IdentityResolvingTo(Guid.NewGuid()); // role resolves to ≥1 active user
        var branch = BranchWithChildPool(AssignmentClauseMode.ByRole, "[\"REVIEWER\"]");

        var report = await ValidateActivate(identity, branch);

        report.Violations.Should().NotContain(v => v.Detail.Contains(ClaimantViolationMarker),
            "a ByRole pool with at least one active holder is claimable and must not be rejected");
    }

    [Fact]
    public async Task ByRolePool_WithNoActiveClaimant_RaisesRB5()
    {
        var identity = IdentityResolvingTo(); // role resolves to nobody
        var branch = BranchWithChildPool(AssignmentClauseMode.ByRole, "[\"REVIEWER\"]");

        var report = await ValidateActivate(identity, branch);

        report.IsValid.Should().BeFalse();
        report.Violations.Should().Contain(
            v => v.Rule == "RB5"
                 && v.Detail.Contains(ClaimantViolationMarker)
                 && v.Detail.Contains("REVIEWER"),
            "a ByRole pool that resolves to no active user must be rejected by RB5, naming the role code");
    }

    [Fact]
    public async Task ByRolePool_LowercaseCode_CanonicalizesBeforeResolving()
    {
        // The hop must upper-case the code before resolving; a lowercase-authored role must not be treated
        // as a different (always-empty) code and false-rejected when the role DOES have a holder.
        var identity = IdentityResolvingTo(Guid.NewGuid());
        var branch = BranchWithChildPool(AssignmentClauseMode.ByRole, "[\"reviewer\"]");

        await ValidateActivate(identity, branch);

        await identity.Received(1).ResolveActiveUsersInRolesAsync(
            Arg.Is<IReadOnlyList<string>>(codes => codes.Contains("REVIEWER")),
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ByPermissionPool_SkipsClaimantCheck()
    {
        // ByPermission has no reverse user lookup → the claimant hop must NOT run for it (no false-reject,
        // no wasted identity round-trip). Use a real perm code so structural RB5/R3 also passes.
        var identity = IdentityResolvingTo(); // would fire a violation IF the hop ran
        var branch = BranchWithChildPool(
            AssignmentClauseMode.ByPermission, $"[\"{PermCodes.Workflow.Task.Process}\"]");

        var report = await ValidateActivate(identity, branch);

        report.Violations.Should().NotContain(v => v.Detail.Contains(ClaimantViolationMarker),
            "a ByPermission pool is never subjected to the ByRole claimant check");
        await identity.DidNotReceive().ResolveActiveUsersInRolesAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MixedRoleAndPermissionPool_SkipsClaimantCheck()
    {
        // A child with ANY ByPermission clause may have unenumerable holders → conservatively skip the whole
        // child even though it also carries a ByRole clause, so a 0-holder role does not false-reject it.
        var identity = IdentityResolvingTo();
        var branch = WorkflowParallelBranch.Create(
            DefId, 1, 4, ParallelBranchType.And, null, false, 0);
        var child = WorkflowBranchChild.Create(0, 2, "review-b", "Duyệt B", null, 1);
        var assignment = WorkflowBranchChildAssignment.Create(0);
        assignment.AddClause(AssignmentClauseMode.ByRole, "[\"REVIEWER\"]");
        assignment.AddClause(AssignmentClauseMode.ByPermission, $"[\"{PermCodes.Workflow.Task.Process}\"]");
        child.SetAssignment(assignment);
        branch.AddChild(child);

        var report = await ValidateActivate(identity, branch);

        report.Violations.Should().NotContain(v => v.Detail.Contains(ClaimantViolationMarker));
        await identity.DidNotReceive().ResolveActiveUsersInRolesAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
