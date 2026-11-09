using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Identity.Domain.Tests.Signing;

/// <summary>
/// Unit tests for the <see cref="SigningRequest"/> workflow aggregate — factory validation, the link/creation-
/// failure transitions, and the once-only ordering invariants that keep the read-time projection coherent.
/// </summary>
public sealed class SigningRequestTests
{
    private static readonly Guid Tenant   = Guid.NewGuid();
    private static readonly Guid Signer   = Guid.NewGuid();
    private static readonly Guid Initiator = Guid.NewGuid();
    private static readonly Guid FixedPublicId = Guid.Parse("0199aaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private static IGuidGenerator Gen()
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(FixedPublicId);
        return gen;
    }

    private static SigningRequest CreateValid() => SigningRequest.Create(
        Gen(), Tenant, SigningArtifactKind.FormPdf, "signing-original/key.pdf",
        "remote-viettel-mysign", Signer, Initiator, placement: "{\"page\":1}");

    [Fact]
    public void Create_SetsImmutableIdentity_AndStartsUnlinked()
    {
        var req = CreateValid();

        req.PublicId.Should().Be(FixedPublicId);
        req.TenantId.Should().Be(Tenant);
        req.ArtifactKind.Should().Be(SigningArtifactKind.FormPdf);
        req.ArtifactRef.Should().Be("signing-original/key.pdf");
        req.ProviderKey.Should().Be("remote-viettel-mysign");
        req.AssignedSignerId.Should().Be(Signer);
        req.Placement.Should().Be("{\"page\":1}");
        req.UnderlyingKind.Should().Be(SigningUnderlyingKind.None);
        req.UnderlyingRecordId.Should().BeNull();
        req.FailureReason.Should().BeNull();
        req.CreatedBy.Should().Be(Initiator);
    }

    [Theory]
    [InlineData("", "p", "ref")]   // empty provider via blank
    public void Create_Throws_OnBlankProviderKey(string provider, string _, string artifactRef)
    {
        var act = () => SigningRequest.Create(
            Gen(), Tenant, SigningArtifactKind.DetachedData, artifactRef, provider, Signer, Initiator);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_Throws_OnEmptyTenant()
    {
        var act = () => SigningRequest.Create(
            Gen(), Guid.Empty, SigningArtifactKind.DetachedData, "ref", "k", Signer, Initiator);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_Throws_OnBlankArtifactRef()
    {
        var act = () => SigningRequest.Create(
            Gen(), Tenant, SigningArtifactKind.DetachedData, "  ", "k", Signer, Initiator);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_Throws_OnEmptyAssignedSigner()
    {
        var act = () => SigningRequest.Create(
            Gen(), Tenant, SigningArtifactKind.DetachedData, "ref", "k", Guid.Empty, Initiator);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void LinkUnderlying_SetsKindAndRecordId()
    {
        var req = CreateValid();

        req.LinkUnderlying(SigningUnderlyingKind.PendingTransaction, "txn-123", Initiator);

        req.UnderlyingKind.Should().Be(SigningUnderlyingKind.PendingTransaction);
        req.UnderlyingRecordId.Should().Be("txn-123");
        req.ModifiedBy.Should().Be(Initiator);
    }

    [Fact]
    public void LinkUnderlying_Throws_WhenAlreadyLinked()
    {
        var req = CreateValid();
        req.LinkUnderlying(SigningUnderlyingKind.DocFlow, "42", Initiator);

        var act = () => req.LinkUnderlying(SigningUnderlyingKind.PendingTransaction, "txn-9", Initiator);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void LinkUnderlying_Throws_OnNoneKind()
    {
        var req = CreateValid();
        var act = () => req.LinkUnderlying(SigningUnderlyingKind.None, "x", Initiator);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void LinkUnderlying_Throws_WhenCreationFailed()
    {
        var req = CreateValid();
        req.MarkCreationFailed("guard rejected", Initiator);

        var act = () => req.LinkUnderlying(SigningUnderlyingKind.DocFlow, "1", Initiator);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkCreationFailed_SetsReason()
    {
        var req = CreateValid();
        req.MarkCreationFailed("no strategy matched", Initiator);

        req.FailureReason.Should().Be("no strategy matched");
        req.UnderlyingKind.Should().Be(SigningUnderlyingKind.None);
    }

    [Fact]
    public void MarkCreationFailed_Throws_WhenAlreadyDispatched()
    {
        var req = CreateValid();
        req.LinkUnderlying(SigningUnderlyingKind.DocFlow, "7", Initiator);

        var act = () => req.MarkCreationFailed("late failure", Initiator);
        act.Should().Throw<InvalidOperationException>();
    }

    // ─── SourceFileId: correlation between UploadedPdf signing request and Files module ──────

    [Fact]
    public void Create_UploadedPdf_WithSourceFileId_StoresIt()
    {
        var fileId = Guid.NewGuid();

        var req = SigningRequest.Create(
            Gen(), Tenant, SigningArtifactKind.UploadedPdf,
            "ref-doc-id", "usb-vgca", Signer, Initiator,
            sourceFileId: fileId);

        req.SourceFileId.Should().Be(fileId,
            "UploadedPdf requests must store the Files module file id for the steps read surface");
    }

    [Fact]
    public void Create_FormPdf_IgnoresSourceFileId_StoresNull()
    {
        var fileId = Guid.NewGuid();

        var req = SigningRequest.Create(
            Gen(), Tenant, SigningArtifactKind.FormPdf,
            "doc-ref", "usb-vgca", Signer, Initiator,
            sourceFileId: fileId);

        req.SourceFileId.Should().BeNull(
            "FormPdf artifacts are not backed by a Files module record; SourceFileId must be null");
    }

    [Fact]
    public void Create_DetachedData_IgnoresSourceFileId_StoresNull()
    {
        var fileId = Guid.NewGuid();

        var req = SigningRequest.Create(
            Gen(), Tenant, SigningArtifactKind.DetachedData,
            "data-ref", "usb-vgca", Signer, Initiator,
            sourceFileId: fileId);

        req.SourceFileId.Should().BeNull(
            "DetachedData artifacts have no PDF storage; SourceFileId must be null");
    }

    [Fact]
    public void Create_UploadedPdf_NullSourceFileId_StoresNull()
    {
        var req = SigningRequest.Create(
            Gen(), Tenant, SigningArtifactKind.UploadedPdf,
            "doc-ref", "usb-vgca", Signer, Initiator,
            sourceFileId: null);

        req.SourceFileId.Should().BeNull(
            "pre-existing UploadedPdf requests created without forwarding the fileId have null SourceFileId (forward-only migration)");
    }
}
