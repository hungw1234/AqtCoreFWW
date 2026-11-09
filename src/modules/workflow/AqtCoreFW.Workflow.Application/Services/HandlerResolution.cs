using AqtCoreFW.Workflow.Domain.Enums;

namespace AqtCoreFW.Workflow.Application.Services;

/// <summary>
/// One resolved pool descriptor. <see cref="Ref"/> is the canonical principal string
/// (see <see cref="CandidatePrincipalSerializer"/>); for the MVP <see cref="Type"/> is
/// always <see cref="CandidatePrincipalType.Permission"/>. <see cref="Combine"/> carries
/// the originating assignment's intra-clause combine for snapshot fidelity (MVP: Union only —
/// Intersect has no producer and is rejected upstream).
/// </summary>
public sealed record CandidatePredicate(
    CandidatePrincipalType Type,
    string Ref,
    AssignmentCombineMode Combine);

/// <summary>
/// Model B resolution outcome (Phase-2, plan 260519-0421 v2 + addendum §3).
///
/// Binary by design — exactly one of:
///   • <see cref="Assignee"/> set         → a single concrete user handles the task,
///   • <see cref="CandidatePredicates"/>  → a pool (predicate-described members claim it),
///   • <see cref="IsEmpty"/>              → nothing resolvable; the caller (P3) decides
///     whether that is legal (final state) or a WF_NO_HANDLER error (non-final).
///
/// Multi-user / mixed user+predicate has no MVP producer and is rejected by the resolver
/// with <see cref="NotSupportedException"/> rather than represented here.
/// </summary>
public sealed record HandlerResolution
{
    public Guid? Assignee { get; private init; }

    public IReadOnlyList<CandidatePredicate> CandidatePredicates { get; private init; } = [];

    public bool IsEmpty => Assignee is null && CandidatePredicates.Count == 0;

    public static HandlerResolution ToAssignee(Guid userId) => new() { Assignee = userId };

    public static HandlerResolution Pool(IReadOnlyList<CandidatePredicate> predicates) =>
        new() { CandidatePredicates = predicates };

    public static readonly HandlerResolution Empty = new();
}
