namespace AqtCoreFW.Workflow.Domain.Enums;

/// <summary>
/// Principal kind projected into a WorkflowTaskCandidateIndex row. MVP inbox query
/// matches Permission only; Role/Group/OrgUnit retained for the deferred resolver
/// modes (RT2-15) and future FE-config plan.
/// </summary>
public enum CandidatePrincipalType
{
    Permission = 0,
    Role = 1,
    Group = 2,
    OrgUnit = 3
}
