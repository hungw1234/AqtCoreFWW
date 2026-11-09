namespace AqtCoreFW.Identity.Domain.Services;

/// <summary>
/// Binary layout of signature bytes returned by a provider.
/// Tier-1 = Raw (VGCA <c>vgca_sign_msg</c> returns raw RSA/ECDSA over precomputed hash + separate <c>CertBase64</c>;
///               Viettel Mysign <c>signHash</c> returns similar raw bytes);
/// Tier-2 = <see cref="XmlDsig"/> | <see cref="Jws"/> (structured, self-contained document);
/// Tier-3 doc-flow = no bytes on BE side (VGCA SignService uploads signed PDF directly to blob storage
///                   via the <c>FileUploadHandler</c> endpoint — use <see cref="IDocumentSigningWorkflow"/>).
/// </summary>
public enum SignatureFormat
{
    /// <summary>
    /// Raw RSA/ECDSA signature over a precomputed hash (Tier-1).
    /// Signing certificate returned separately in <see cref="SignResult.SigningCertificate"/>
    /// or via a <c>certificates/info</c> call.
    /// Declared by: VGCA USB token (<c>usb-vgca</c>) and Viettel Mysign (<c>remote-viettel-mysign</c>).
    /// </summary>
    Raw = 0,

    /// <summary>
    /// PKCS#7 / CMS detached signature (legacy default — preserved for backward compatibility).
    /// Used by <see cref="IDigitalSignatureProvider.VerifyAsync"/> when <see cref="VerifyRequest.Format"/>
    /// is not specified (default value).
    /// </summary>
    Pkcs7 = 1,

    /// <summary>
    /// XMLDSig-signed XML document (Tier-2 — VGCA <c>vgca_sign_xml</c>).
    /// Signature bytes form a self-contained, schema-valid XMLDSig document.
    /// Verified by <see cref="IStructuredSignatureService.VerifyXmlDsigAsync"/>.
    /// </summary>
    XmlDsig = 2,

    /// <summary>
    /// JWS compact serialization over JSON content (Tier-2 — VGCA <c>vgca_sign_json</c>).
    /// Signing certificate embedded in the <c>x5c</c> JWS header.
    /// Verified by <see cref="IStructuredSignatureService.VerifyJwsAsync"/>.
    /// </summary>
    Jws = 3,
}
