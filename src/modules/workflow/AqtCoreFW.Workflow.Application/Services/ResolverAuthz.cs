using AqtCoreFW.Workflow.Domain.Enums;

namespace AqtCoreFW.Workflow.Application.Services;

/// <summary>
/// The actor's resolved principals, used to match against a pool descriptor. Mirrors the four
/// principal sets the inbox/claim path loads (<c>GetInboxQueryHandler</c> / <c>IsPoolEligibleAsync</c>):
/// effective permission codes, direct role codes, direct group codes, active org-unit codes — all
/// canonical (UPPER+TRIM) the same way the candidate snapshot stores refs.
/// </summary>
internal sealed record ActorPrincipals(
    IReadOnlySet<string> PermissionCodes,
    IReadOnlySet<string> RoleCodes,
    IReadOnlySet<string> GroupCodes,
    IReadOnlySet<string> OrgUnitCodes);

/// <summary>
/// Shared authorization check for the Model B handler resolver.
/// Determines whether the actor is authorized given a resolved pool descriptor.
///
/// Rules (fail-CLOSED by default — caller returns 403 if IsAuthorized returns false):
///   • Assignee branch: resolution named a single concrete user → only that user is authorized.
///   • Pool branch: resolution named predicates → actor is authorized iff ANY predicate matches the
///     actor's principal set OF THE PREDICATE'S TYPE (Permission/Role/Group/OrgUnit). This keeps
///     transition authz IN PARITY with claim eligibility — a pool task that is inbox-visible and
///     claimable is also actionable. (Previously matched permission codes only, which silently
///     denied ByRole/ByGroup/ByOrgUnit transitions to users who held the role/group/org-unit.)
///   • Empty branch: no handler resolved → caller treats this as WF_NO_HANDLER (422), not authz.
///
/// Phase-3 Slice-5: extracted to avoid duplicating the same logic across the transition handlers.
/// </summary>
internal static class ResolverAuthz
{
    /// <summary>
    /// Returns true when <paramref name="actorId"/> satisfies the resolved handler pool.
    /// Returns false (deny) for empty resolutions — callers must handle the empty case separately
    /// (WF_NO_HANDLER pre-check) before calling this method for the transition-authz gate.
    /// </summary>
    /// <param name="resolution">Resolver output for the target state.</param>
    /// <param name="actor">The actor's four principal sets (permission/role/group/org-unit).</param>
    /// <param name="actorId">The user attempting the action.</param>
    internal static bool IsActorAuthorized(
        HandlerResolution resolution,
        ActorPrincipals actor,
        Guid actorId)
    {
        // Assignee branch — single concrete user named
        if (resolution.Assignee.HasValue)
            return resolution.Assignee.Value == actorId;

        // Pool branch — at least one predicate must match the actor's principals OF THAT TYPE
        if (resolution.CandidatePredicates.Count > 0)
            return resolution.CandidatePredicates.Any(p => MatchesPrincipal(p, actor));

        // Empty — caller is responsible for WF_NO_HANDLER gate; deny here as a safety net
        return false;
    }

    private static bool MatchesPrincipal(CandidatePredicate predicate, ActorPrincipals actor) =>
        predicate.Type switch
        {
            CandidatePrincipalType.Permission => actor.PermissionCodes.Contains(predicate.Ref),
            CandidatePrincipalType.Role => actor.RoleCodes.Contains(predicate.Ref),
            CandidatePrincipalType.Group => actor.GroupCodes.Contains(predicate.Ref),
            CandidatePrincipalType.OrgUnit => actor.OrgUnitCodes.Contains(predicate.Ref),
            _ => false,
        };
}
