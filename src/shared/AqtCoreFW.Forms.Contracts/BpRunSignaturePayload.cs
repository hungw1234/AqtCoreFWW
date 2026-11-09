namespace AqtCoreFW.SharedKernel.Contracts.Clients;

/// <summary>
/// Transport-neutral digital-signature proof bundle passed across the BusinessProcess → Forms
/// module boundary when a BP-Run form (intake or state form) carries a DigitalSignature field.
///
/// Carries the signature bytes + signer certificate ONLY — never the signed content. The signed
/// content (canonical bytes) is always recomputed server-side from the submitted data inside the
/// Forms submit handler, so a client cannot dictate "what was signed" (QĐ-742 integrity invariant).
///
/// Lives in the contracts assembly (not Forms.Application) so the BusinessProcess module — which
/// references Forms.Contracts but NOT Forms.Application — can supply it. The boundary implementation
/// (<c>InProcessFormSubmissionRunClient</c>) maps it to the Forms.Application <c>SignaturePayload</c>
/// at the single point it builds the submit command. Byte arrays cross the boundary directly,
/// mirroring the sibling <c>SignatureVerifyRequest</c> convention in the Identity contracts.
/// </summary>
public sealed record BpRunSignaturePayload(
    byte[] Signature,
    byte[]? SignerCertDer,
    string Format = "Raw",
    string HashAlg = "SHA256",
    string Encode = "RSA",
    string? ProviderKey = null);
