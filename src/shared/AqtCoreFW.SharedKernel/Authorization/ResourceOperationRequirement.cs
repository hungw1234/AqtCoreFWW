using Microsoft.AspNetCore.Authorization;

namespace AqtCoreFW.SharedKernel.Authorization;

/// <summary>
/// Golden-path base for resource-based authorization across all modules.
///
/// Usage pattern (implement in each module):
///   public class CaseOperationRequirement : ResourceOperationRequirement { ... }
///   public class CaseAuthorizationHandler
///       : AuthorizationHandler&lt;CaseOperationRequirement, Case&gt; { ... }
///
/// Check order inside handler:
///   1. RBAC fast path: ctx.User.IsInRole("Admin") → Succeed
///   2. ABAC: query AttributeRules from ICacheService (5 min TTL)
///   3. Resource ownership: resource.OwnerId == currentUserId
///
/// Usage in command handler:
///   var result = await _authService.AuthorizeAsync(user, entity, new CaseOperationRequirement("Submit"));
///   if (!result.Succeeded) return Result.Fail(new ForbiddenError("Access denied"));
/// </summary>
public abstract class ResourceOperationRequirement : IAuthorizationRequirement
{
    public string Operation { get; }

    protected ResourceOperationRequirement(string operation)
        => Operation = operation;
}

/// <summary>
/// Common CRUD operation constants — modules may add domain-specific operations.
/// </summary>
public static class Operations
{
    public const string Read = "Read";
    public const string Create = "Create";
    public const string Update = "Update";
    public const string Delete = "Delete";
    public const string Export = "Export";
    public const string Submit = "Submit";
    public const string Approve = "Approve";
}
