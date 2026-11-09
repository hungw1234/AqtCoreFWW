namespace AqtCoreFW.SharedKernel.Application;

/// <summary>
/// Opt-in marker for a command that is intentionally NOT tenant-scoped, so
/// <see cref="Behaviors.TenantAwareBehavior{TRequest,TResponse}"/> does NOT require an ambient tenant
/// for it.
///
/// <para><b>Use this only for system-to-system commands whose tenant is derived from a persisted
/// entity, not from the request principal</b> — e.g. an <c>[AllowAnonymous]</c> vendor webhook
/// authenticated by an inbound token and keyed by a globally-unique, unguessable identifier. A command
/// carrying this marker takes FULL responsibility for its own tenant safety (exact-match lookup, no
/// blind cross-tenant mutation); the pipeline no longer enforces it.</para>
///
/// <para>Default to NOT using this. A command that has an ambient tenant (any authenticated user or
/// SystemAdmin flow) must remain a plain <see cref="IBaseCommand"/> so tenant enforcement applies.
/// Every use is reviewed against the bypass test in <c>TenantAwareBehaviorTests</c>.</para>
/// </summary>
public interface ITenantAgnosticCommand : IBaseCommand;
