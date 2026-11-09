namespace AqtCoreFW.Forms.Application.Commands.SubmitForm;

/// <summary>
/// Proof bundle supplied by the client when submitting a form that requires a digital signature.
/// This record carries the signature bytes and signer certificate — NOT the signed content.
/// The signed content (canonical bytes) is always recomputed server-side from <c>request.Data</c>
/// to prevent any client-supplied "what was signed" from being trusted (QĐ-742 integrity invariant).
/// Reused by both <see cref="SubmitFormCommand"/> (authenticated) and
/// <see cref="SubmitPublicForm.SubmitPublicFormCommand"/> (anonymous).
/// </summary>
public sealed record SignaturePayload(
    /// <summary>Raw signature bytes produced by the signing device/provider.</summary>
    byte[] Signature,
    /// <summary>DER-encoded signer certificate. Required for Raw format (cert not embedded in Pkcs7).</summary>
    byte[]? SignerCertDer,
    /// <summary>Signature format — "Raw" | "Pkcs7" | "XmlDsig" | "Jws". Passed verbatim to IIdentitySigningClient.</summary>
    string Format = "Raw",
    /// <summary>Hash algorithm used during signing — e.g. "SHA256", "SHA384".</summary>
    string HashAlg = "SHA256",
    /// <summary>Asymmetric algorithm — "RSA" | "EC" (ECDSA). Required for Raw verify.</summary>
    string Encode = "RSA",
    /// <summary>
    /// Optional provider key (e.g. "usb-vgca", "remote-viettel-mysign").
    /// When null the signing client falls back to the tenant's configured default provider.
    /// Callers should pass the same provider that drove signing to avoid a provider-mismatch reject.
    /// </summary>
    string? ProviderKey = null);
