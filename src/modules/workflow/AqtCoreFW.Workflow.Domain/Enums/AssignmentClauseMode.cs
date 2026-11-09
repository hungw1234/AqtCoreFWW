namespace AqtCoreFW.Workflow.Domain.Enums;

/// <summary>
/// Principal-resolution mode of one WorkflowTransitionAssignmentClause.
/// MVP-implemented (have a producer via RPC→ByPermission migration + seed):
///   SpecificUser, Initiator, ByPermission.
/// RT2-15-shipped (producer + R4 lift):
///   PreviousHandler (Phase 01 v2.137p108),
///   ByRole (Phase 03 B1 v2.137p109 + read-side v2.137p110),
///   ByGroup (Phase 02 v2.137p112 — Save Q2 validation + predicate-pool producer;
///            READ-SIDE GAP: claim/inbox unclaimable until T3-WF-CANDIDATEINDEX-READSIDE-EXT-GROUP ships).
/// Deferred — schema/enum retained, resolver throws NotSupportedException until an
/// FE config-UI producer + R4 lift ships (RT2-15 Phase 04):
///   ByOrgUnit.
/// </summary>
public enum AssignmentClauseMode
{
    SpecificUser = 0,
    Initiator = 1,
    PreviousHandler = 2,
    ByPermission = 3,
    ByRole = 4,
    ByGroup = 5,
    ByOrgUnit = 6
}
