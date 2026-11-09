namespace AqtCoreFW.SharedKernel.Authorization;

/// <summary>
/// ABAC policy name constants — attribute-based access control.
/// Permission-based policies use PermCodes class instead.
/// </summary>
public static class Policies
{
    /// <summary>Requires matching DepartmentCode claim — enforced by DepartmentAccessHandler.</summary>
    public const string DepartmentRestricted = nameof(DepartmentRestricted);

    /// <summary>Requires sufficient ClearanceLevel — enforced by ClassificationAccessHandler.</summary>
    public const string ClassifiedAccess = nameof(ClassifiedAccess);

    /// <summary>
    /// Grants access to the Mode-2 signature document-hash endpoint to either a standalone form
    /// submitter or a business-process-run filler — enforced by AnyPermissionAuthorizationHandler
    /// over (FORMS.SUBMIT.CREATE, BUSINESSPROCESS.BP.VIEW). The endpoint only returns a hash of
    /// caller-supplied data for a template the caller already renders, so both surfaces are equivalent.
    /// </summary>
    public const string SignatureDocHashAccess = nameof(SignatureDocHashAccess);
}

/// <summary>Default role names — seeded on startup.</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string GovOfficer = "GovOfficer";
    public const string Citizen = "Citizen";
    public const string SystemAdmin = "SystemAdmin";
}
