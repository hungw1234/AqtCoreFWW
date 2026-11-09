using AqtCoreFW.Workflow.Contracts.Validation;

namespace AqtCoreFW.Workflow.Infrastructure.Validation;

/// <summary>
/// Default contributor for the generic "Generic" entity type — the system default and the only
/// contributor registered out of the box. Lets the framework expose a working
/// CreateWorkflowDefinition / SaveDefinitionGraph surface without a domain-specific contributor.
/// <para>
/// The allowed state names mirror the canonical master-data taxonomy
/// (<c>MasterData.Domain.Constants.WorkflowTaxonomyCodes.WorkflowStates</c>). The list is duplicated
/// here intentionally — Workflow.Infrastructure must not reference MasterData.Domain (module
/// isolation). Keep the two in sync when the baseline taxonomy changes.
/// </para>
/// Downstream consumers may register their own contributor with a domain-specific EntityType string
/// (e.g., "BuildingPermit", "BusinessLicense") instead of relying on this.
/// </summary>
public sealed class GenericEntityStatusEnumContributor : IEntityStatusEnumContributor
{
    public string EntityType => "Generic";

    public IReadOnlyCollection<string> GetStateNames() =>
    [
        "Draft",
        "Submitted",
        "UnderReview",
        "Assigned",
        "Approved",
        "Rejected",
        "Returned",
        "ReturnedForRevision",
        "Closed",
        "Cancelled",
    ];
}
