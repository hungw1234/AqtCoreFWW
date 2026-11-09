using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Forms.Domain.Entities;

/// <summary>
/// Per-role signature evidence row for multi-signer form submissions.
/// One row per signer role that has completed signing. Mirrors the scalar evidence
/// block on FormSubmission but is keyed by SignerRoleKey so N signers each get
/// their own verifiable record.
///
/// Single-signer evidence continues to live on FormSubmission's scalar fields
/// (SignatureVerdict … SignerCertDer) via RecordSignatureEvidence — those are
/// unchanged. This child collection activates only when template.SignerRoles.Count > 1.
///
/// T1 entity: non-generic Entity (long Id DB-assigned).
/// </summary>
public sealed class FormSubmissionSignerEvidence : Entity
{
    public long FormSubmissionId { get; private set; }

    /// <summary>Matches FormTemplateSignerRole.RoleKey — identifies which role this evidence belongs to.</summary>
    public string SignerRoleKey { get; private set; } = string.Empty;

    /// <summary>Outcome of verify-at-ingest for this role's signer. True = verified valid.</summary>
    public bool SignatureVerdict { get; private set; }

    /// <summary>Signer certificate subject DN captured at verify time.</summary>
    public string? SignerSubject { get; private set; }

    /// <summary>Signer certificate serial number.</summary>
    public string? SignerSerial { get; private set; }

    /// <summary>Server UTC instant the signature was verified for this role.</summary>
    public DateTimeOffset SignatureVerifiedAt { get; private set; }

    /// <summary>
    /// Deterministic JSON of the validated chain + OCSP outcome (audit-grade re-verify evidence).
    /// nvarchar(max) — see EF configuration.
    /// </summary>
    [DataClassification(DataClassificationLevel.Internal)]
    public string? SignatureChainEvidence { get; private set; }

    /// <summary>Signer certificate DER bytes — kept for offline re-verification (public cert, not secret).</summary>
    public byte[]? SignerCertDer { get; private set; }

    /// <summary>Authenticated user who provided this role's signature. Null = anonymous/guest path.</summary>
    public Guid? SignerUserId { get; private set; }

    /// <summary>Client-reported UTC instant the signer applied the signature (before ingest verify).</summary>
    public DateTimeOffset SignedAt { get; private set; }

    private FormSubmissionSignerEvidence() { } // EF Core

    public static FormSubmissionSignerEvidence Create(
        long formSubmissionId,
        string signerRoleKey,
        bool signatureVerdict,
        string? signerSubject,
        string? signerSerial,
        DateTimeOffset signatureVerifiedAt,
        string? signatureChainEvidence,
        byte[]? signerCertDer,
        Guid? signerUserId,
        DateTimeOffset signedAt)
    {
        if (string.IsNullOrWhiteSpace(signerRoleKey))
            throw new ArgumentException("SignerRoleKey không được để trống.", nameof(signerRoleKey));

        return new FormSubmissionSignerEvidence
        {
            FormSubmissionId = formSubmissionId,
            SignerRoleKey = signerRoleKey,
            SignatureVerdict = signatureVerdict,
            SignerSubject = signerSubject,
            SignerSerial = signerSerial,
            SignatureVerifiedAt = signatureVerifiedAt,
            SignatureChainEvidence = signatureChainEvidence,
            SignerCertDer = signerCertDer,
            SignerUserId = signerUserId,
            SignedAt = signedAt
        };
    }
}
