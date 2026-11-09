namespace AqtCoreFW.SharedKernel.Compliance;

/// <summary>
/// Suppresses the automatic audit row emitted by <c>AuditBehavior</c> for the decorated command.
/// Use on commands that emit their own structured audit entry (e.g. signing commands that must
/// record provider, cert serial, payload hash, OCSP result — fields not available to the generic
/// AuditBehavior pipeline).
///
/// The command handler is responsible for calling <c>IAuditLogger.LogCommandAsync</c> manually,
/// including the <c>dataSnapshot</c> with signing-specific evidence.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class SuppressAutoAuditAttribute : Attribute { }
