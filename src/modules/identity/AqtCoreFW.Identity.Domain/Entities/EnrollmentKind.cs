namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>
/// Provider kind for a <see cref="SignerCertEnrollment"/>. Records provenance — which signing
/// infrastructure the enrolled certificate belongs to. Does NOT affect binding logic (binding stays
/// provider-agnostic via serial+issuer/thumbprint); used for enrollment flow routing (P2) and audit.
/// </summary>
public enum EnrollmentKind
{
    /// <summary>Remote Viettel Mysign: server-orchestrated OTP-triggered signing.</summary>
    RemoteViettelMysign = 1,

    /// <summary>USB Token VGCA/BCY: client-side plugin signing (vgca_sign_msg).</summary>
    UsbTokenVgca = 2,
}
