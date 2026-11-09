using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Forms.Domain.Tests.Entities;

/// <summary>
/// Unit tests for FormSubmissionSignerEvidence.Create factory and
/// FormSubmission.RecordSignerEvidence evidence-collection semantics.
/// Covers per-role evidence creation, optional-field nullability, additive-first
/// independence of scalar vs collection evidence paths, and null-guard on append.
/// </summary>
public sealed class FormSubmissionSignerEvidenceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId  = Guid.NewGuid();

    private static readonly IGuidGenerator Gen = CreateGen();
    private static IGuidGenerator CreateGen()
    {
        var g = Substitute.For<IGuidGenerator>();
        g.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        g.NewIdRandom().Returns(_ => Guid.NewGuid());
        return g;
    }

    // ── FormSubmissionSignerEvidence.Create ──────────────────────────────────

    [Fact]
    public void FormSubmissionSignerEvidence_Create_WithValidInputs_Succeeds()
    {
        var submissionId = 1L;
        var roleKey      = "approver";
        var verdict      = true;
        var subject      = "CN=Nguyen Van A, OID.2.5.4.5=079201001234";
        var serial       = "SERIAL-001";
        var verifiedAt   = DateTimeOffset.UtcNow;
        var chainJson    = "{\"chain\":[\"root\",\"leaf\"]}";
        var certDer      = new byte[] { 0xAB, 0xCD, 0xEF };
        var signerUserId = Guid.NewGuid();
        var signedAt     = DateTimeOffset.UtcNow.AddSeconds(-30);

        var evidence = FormSubmissionSignerEvidence.Create(
            submissionId,
            roleKey,
            verdict,
            subject,
            serial,
            verifiedAt,
            chainJson,
            certDer,
            signerUserId,
            signedAt);

        evidence.Should().NotBeNull();
        evidence.FormSubmissionId.Should().Be(submissionId);
        evidence.SignerRoleKey.Should().Be(roleKey);
        evidence.SignatureVerdict.Should().BeTrue();
        evidence.SignerSubject.Should().Be(subject);
        evidence.SignerSerial.Should().Be(serial);
        evidence.SignatureVerifiedAt.Should().Be(verifiedAt);
        evidence.SignatureChainEvidence.Should().Be(chainJson);
        evidence.SignerCertDer.Should().BeEquivalentTo(certDer);
        evidence.SignerUserId.Should().Be(signerUserId);
        evidence.SignedAt.Should().Be(signedAt);
    }

    [Fact]
    public void FormSubmissionSignerEvidence_Create_WithNullOptionalFields_Succeeds()
    {
        var evidence = FormSubmissionSignerEvidence.Create(
            formSubmissionId:       2L,
            signerRoleKey:          "witness",
            signatureVerdict:       false,
            signerSubject:          null,
            signerSerial:           null,
            signatureVerifiedAt:    DateTimeOffset.UtcNow,
            signatureChainEvidence: null,
            signerCertDer:          null,
            signerUserId:           null, // Anonymous signer
            signedAt:               DateTimeOffset.UtcNow);

        evidence.Should().NotBeNull();
        evidence.SignerSubject.Should().BeNull();
        evidence.SignerSerial.Should().BeNull();
        evidence.SignatureChainEvidence.Should().BeNull();
        evidence.SignerCertDer.Should().BeNull();
        evidence.SignerUserId.Should().BeNull();
        evidence.SignatureVerdict.Should().BeFalse();
    }

    [Fact]
    public void FormSubmissionSignerEvidence_Create_WithEmptyRoleKey_Throws()
    {
        var action = () => FormSubmissionSignerEvidence.Create(
            formSubmissionId:       1L,
            signerRoleKey:          "",
            signatureVerdict:       true,
            signerSubject:          null,
            signerSerial:           null,
            signatureVerifiedAt:    DateTimeOffset.UtcNow,
            signatureChainEvidence: null,
            signerCertDer:          null,
            signerUserId:           null,
            signedAt:               DateTimeOffset.UtcNow);

        action.Should().Throw<ArgumentException>()
            .WithMessage("*SignerRoleKey*");
    }

    // ── FormSubmission.RecordSignerEvidence ──────────────────────────────────

    [Fact]
    public void FormSubmission_RecordSignerEvidence_AppendsToCollection()
    {
        var submission = CreateTestSubmission();

        var evidence1 = FormSubmissionSignerEvidence.Create(
            submission.Id, "approver", true,
            "CN=Approver", "SERIAL-1",
            DateTimeOffset.UtcNow, null, null, Guid.NewGuid(), DateTimeOffset.UtcNow);

        var evidence2 = FormSubmissionSignerEvidence.Create(
            submission.Id, "witness", true,
            "CN=Witness", "SERIAL-2",
            DateTimeOffset.UtcNow, null, null, null, DateTimeOffset.UtcNow);

        submission.RecordSignerEvidence(evidence1);
        submission.RecordSignerEvidence(evidence2);

        submission.SignerEvidences.Should().HaveCount(2);
        submission.SignerEvidences.Should().Satisfy(
            e => e.SignerRoleKey == "approver" && e.SignerUserId != null,
            e => e.SignerRoleKey == "witness"  && e.SignerUserId == null);
    }

    [Fact]
    public void FormSubmission_RecordSignerEvidence_WithNullEvidence_Throws()
    {
        var submission = CreateTestSubmission();

        var action = () => submission.RecordSignerEvidence(null!);

        action.Should().Throw<ArgumentNullException>();
    }

    // ── Additive-first: scalar and collection evidence are independent ────────

    [Fact]
    public void FormSubmission_ScalarEvidenceAndCollectionIndependent()
    {
        var submission = CreateTestSubmission();

        // Scalar path (single-signer)
        submission.RecordSignatureEvidence(
            verdict:            true,
            signerSubject:      "CN=SingleSigner",
            signerSerial:       "SERIAL-SS",
            verifiedAt:         DateTimeOffset.UtcNow,
            chainEvidenceJson:  null,
            signerCertDer:      new byte[] { 0x01, 0x02 });

        // Collection still empty after scalar write
        submission.SignerEvidences.Should().BeEmpty();
        submission.SignatureVerdict.Should().Be(true);

        // Collection path (multi-signer) coexists with scalar
        var evidence = FormSubmissionSignerEvidence.Create(
            submission.Id, "approver", true,
            "CN=Approver", "SERIAL-APPR",
            DateTimeOffset.UtcNow, null, null, Guid.NewGuid(), DateTimeOffset.UtcNow);

        submission.RecordSignerEvidence(evidence);

        submission.SignatureVerdict.Should().Be(true);
        submission.SignerEvidences.Should().ContainSingle();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static FormSubmission CreateTestSubmission()
    {
        var template = FormTemplate.Create(Gen, TenantId, "Test", "Test VI", "test_form", StorageMode.Json, ActorId);
        return FormSubmission.Create(
            gen:                 Gen,
            tenantId:            TenantId,
            formTemplateId:      template.Id,
            formTemplateVersion: 1,
            submissionData:      null,
            submittedBy:         ActorId,
            consentGiven:        false);
    }
}
