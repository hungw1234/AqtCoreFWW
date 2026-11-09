namespace AqtCoreFW.SharedKernel.Abstractions;

/// <summary>
/// Decrypts an application-level encrypted field value (the <c>v{N}:</c> AES-256-GCM wire format
/// written by the EF value converter) for read-model handlers that bypass EF.
///
/// <para>
/// Dapper read-side queries select PII columns (e.g. AspNetUsers.Email) directly and therefore
/// receive the stored ciphertext — the EF value converter only runs on the EF/UserManager path.
/// Handlers that surface such a column to a caller decrypt it through this abstraction so the
/// read-model returns plaintext consistently with the write-model.
/// </para>
///
/// <para>
/// Lives in SharedKernel (not Infrastructure) so Application-layer handlers can depend on it without
/// referencing Infrastructure; the implementation wraps the Vault key resolver.
/// </para>
/// </summary>
public interface IEncryptedFieldDecryptor
{
    /// <summary>
    /// Decrypts a single encrypted field value. <c>null</c>/empty passes through unchanged. A value
    /// that is not valid ciphertext (e.g. already-plaintext legacy data) is returned unchanged rather
    /// than throwing — a read-model enrichment must never fault a whole list on one bad row.
    /// </summary>
    string? Decrypt(string? cipher);
}
