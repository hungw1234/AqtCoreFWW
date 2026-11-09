using Microsoft.AspNetCore.Authorization;

namespace AqtCoreFW.SharedKernel.Authorization;

/// <summary>
/// Marker requirement: user's department claim must satisfy ABAC AttributeRules in DB.
/// Handler + IAttributeRuleReader live in Identity module (depend on AttributeRule entity).
/// </summary>
public sealed class DepartmentAccessRequirement : IAuthorizationRequirement { }
