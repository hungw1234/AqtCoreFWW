namespace AqtCoreFW.BusinessProcess.Domain.Errors;

/// <summary>
/// GOV_BP_xxx error codes for BusinessProcess module.
/// Reserved range: 001–099. Cross-module guards (e.g. R14 BP-Run tenant check) use 002.
/// Vietnamese-facing messages live at the throw site; this class only fixes the code constants.
/// </summary>
public static class BusinessProcessErrorCodes
{
    /// <summary>BP definition not found (by PublicId).</summary>
    public const string NotFound = "GOV_BP_001";

    /// <summary>R14 cross-tenant guard — instance/definition does not belong to caller's tenant.</summary>
    /// <remarks>Already in use by Forms.InProcessFormSubmissionRunClient (BP MVP P2). Same code, same meaning.</remarks>
    public const string CrossTenantRejected = "GOV_BP_002";

    /// <summary>Duplicate Code per tenant.</summary>
    public const string DuplicateCode = "GOV_BP_003";

    /// <summary>BP is inactive — runtime operations rejected.</summary>
    public const string Inactive = "GOV_BP_004";

    /// <summary>State/Transition form binding violates (BpId, StateId|TransitionId, FormTemplateId) uniqueness.</summary>
    public const string DuplicateBinding = "GOV_BP_005";

    /// <summary>IntakeFormTemplateId and IntakeFormTemplateVersion must be set together.</summary>
    public const string IntakePairingInvalid = "GOV_BP_010";

    /// <summary>WorkflowDefinition PublicId not found for tenant (P7: FE resolves Guid → bigint inside handler).</summary>
    public const string WorkflowDefinitionNotFound = "GOV_BP_011";

    /// <summary>IntakeFormTemplate PublicId not found for tenant (P7: FE resolves Guid → bigint inside handler).</summary>
    public const string IntakeFormTemplateNotFound = "GOV_BP_012";
}
