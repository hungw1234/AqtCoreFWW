namespace AqtCoreFW.SharedKernel.Compliance;

/// <summary>
/// Marks a property that carries a <see cref="DataClassificationAttribute"/> of
/// <see cref="DataClassificationLevel.Restricted"/> but MUST NOT be encrypted at rest
/// by an <c>EncryptedStringConverter</c>.
///
/// Canonical use cases:
///   • <c>PasswordHash</c> — already hashed by ASP.NET Core Identity; re-encrypting
///     would break the entire Identity pipeline.
///   • <c>SecurityStamp</c> — written on every security-relevant event; encrypting
///     would create excessive Vault round-trips and break concurrency detection.
///
/// Wave-6 architecture guard rule:
///   <c>[Restricted] ∧ ¬[DoNotEncryptAtRest] ⇒ EncryptedStringConverter applied</c>
///
/// Document the reason inline when applying this attribute so future readers
/// understand why the exemption exists.
/// </summary>
/// <param name="reason">Human-readable justification for skipping encryption.</param>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class DoNotEncryptAtRestAttribute(string reason) : Attribute
{
    /// <summary>Justification for not encrypting this Restricted property at rest.</summary>
    public string Reason { get; } = reason;
}
