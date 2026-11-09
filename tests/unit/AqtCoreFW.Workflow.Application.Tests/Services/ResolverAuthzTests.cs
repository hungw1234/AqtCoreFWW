using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Domain.Enums;

namespace AqtCoreFW.Workflow.Application.Tests.Services;

/// <summary>
/// Unit tests for ResolverAuthz.IsActorAuthorized — proves type-aware pool matching
/// (Permission/Role/Group/OrgUnit), the fix for the permission-only authz bug that silently
/// denied ByRole/ByGroup/ByOrgUnit transitions to their holders.
/// </summary>
public sealed class ResolverAuthzTests
{
    private static readonly Guid Actor = Guid.NewGuid();

    private static ActorPrincipals Principals(
        string[]? perms = null, string[]? roles = null, string[]? groups = null, string[]? orgUnits = null)
        => new(
            new HashSet<string>(perms ?? [], StringComparer.Ordinal),
            new HashSet<string>(roles ?? [], StringComparer.Ordinal),
            new HashSet<string>(groups ?? [], StringComparer.Ordinal),
            new HashSet<string>(orgUnits ?? [], StringComparer.Ordinal));

    private static HandlerResolution Pool(CandidatePrincipalType type, string @ref)
        => HandlerResolution.Pool([new CandidatePredicate(type, @ref, AssignmentCombineMode.Union)]);

    [Fact]
    public void Permission_Match_Authorized()
        => ResolverAuthz.IsActorAuthorized(
            Pool(CandidatePrincipalType.Permission, "WF.APPROVE"),
            Principals(perms: ["WF.APPROVE"]), Actor).Should().BeTrue();

    [Fact]
    public void Role_Match_Authorized()
        => ResolverAuthz.IsActorAuthorized(
            Pool(CandidatePrincipalType.Role, "GOVOFFICER"),
            Principals(roles: ["GOVOFFICER"]), Actor).Should().BeTrue();

    [Fact]
    public void Group_Match_Authorized()
        => ResolverAuthz.IsActorAuthorized(
            Pool(CandidatePrincipalType.Group, "FINANCE"),
            Principals(groups: ["FINANCE"]), Actor).Should().BeTrue();

    [Fact]
    public void OrgUnit_Match_Authorized()
        => ResolverAuthz.IsActorAuthorized(
            Pool(CandidatePrincipalType.OrgUnit, "OU_EAST"),
            Principals(orgUnits: ["OU_EAST"]), Actor).Should().BeTrue();

    [Fact]
    public void Role_Predicate_MatchedByPermissionCodeOnly_Denied()
        // The role name appearing in the actor's PERMISSION set must NOT authorize a Role predicate —
        // proves matching is keyed by principal TYPE, not cross-type string containment.
        => ResolverAuthz.IsActorAuthorized(
            Pool(CandidatePrincipalType.Role, "GOVOFFICER"),
            Principals(perms: ["GOVOFFICER"]), Actor).Should().BeFalse();

    [Fact]
    public void Role_NoMatch_Denied()
        => ResolverAuthz.IsActorAuthorized(
            Pool(CandidatePrincipalType.Role, "GOVOFFICER"),
            Principals(roles: ["CLERK"]), Actor).Should().BeFalse();

    [Fact]
    public void Assignee_OnlyNamedUser_Authorized()
    {
        var empty = Principals();
        ResolverAuthz.IsActorAuthorized(HandlerResolution.ToAssignee(Actor), empty, Actor).Should().BeTrue();
        ResolverAuthz.IsActorAuthorized(HandlerResolution.ToAssignee(Guid.NewGuid()), empty, Actor).Should().BeFalse();
    }

    [Fact]
    public void Empty_Denied()
        => ResolverAuthz.IsActorAuthorized(HandlerResolution.Empty, Principals(), Actor).Should().BeFalse();
}
