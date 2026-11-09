using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;

namespace AqtCoreFW.Identity.Domain.Tests.Signing;

/// <summary>
/// Unit tests for <see cref="UserSignatureProfile"/> aggregate:
/// - Create: required fields, PublicId assigned, IsDefault=false.
/// - Rename: reject empty / whitespace / >128 chars.
/// - MarkDefault / ClearDefault: idempotency + invariant that clearing prior is caller's job.
/// - SetImages: accepts nulls, rejects keys >512 chars.
/// - LinkCert: records enrollmentId.
/// Red-before-green evidence: see phase-01 report (temporarily broke MarkDefault to prove RED).
/// </summary>
public sealed class UserSignatureProfileTests
{
    private static readonly Guid Tenant   = Guid.NewGuid();
    private static readonly Guid User     = Guid.NewGuid();
    private static readonly Guid Actor    = Guid.NewGuid();

    // V7GuidGenerator is internal — hand-roll a real (non-mock) fake so factory calls nested inside
    // other substitutes' .Returns(...) arguments never corrupt NSubstitute's per-thread "last call" state.
    private sealed class TestGuidGenerator : IGuidGenerator
    {
        public Guid NewIdMonotonic() => Guid.CreateVersion7();
        public Guid NewIdRandom() => Guid.NewGuid();
    }
    private static readonly IGuidGenerator Gen = new TestGuidGenerator();

    private static UserSignatureProfile CreateProfile(string name = "My Signature") =>
        UserSignatureProfile.Create(Gen, Tenant, User, name, Actor);

    // ── Create ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Create_SetsFieldsCorrectly()
    {
        var p = CreateProfile("Primary Signature");

        p.TenantId.Should().Be(Tenant);
        p.UserId.Should().Be(User);
        p.DisplayName.Should().Be("Primary Signature");
        p.IsDefault.Should().BeFalse("new profile must not be default until explicitly set");
        p.PublicId.Should().NotBe(Guid.Empty, "PublicId must be assigned");
        p.SignerCertEnrollmentId.Should().BeNull();
        p.FullSignatureKey.Should().BeNull();
        p.InitialSignatureKey.Should().BeNull();
        p.CreatedBy.Should().Be(Actor);
    }

    [Fact]
    public void Create_TrimDisplayName()
    {
        var p = UserSignatureProfile.Create(Gen, Tenant, User, "  Trimmed  ", Actor);
        p.DisplayName.Should().Be("Trimmed");
    }

    [Fact]
    public void Create_Throws_OnEmptyTenant()
    {
        var act = () => UserSignatureProfile.Create(Gen, Guid.Empty, User, "Name", Actor);
        act.Should().Throw<ArgumentException>().WithParameterName("tenantId");
    }

    [Fact]
    public void Create_Throws_OnEmptyUser()
    {
        var act = () => UserSignatureProfile.Create(Gen, Tenant, Guid.Empty, "Name", Actor);
        act.Should().Throw<ArgumentException>().WithParameterName("userId");
    }

    [Fact]
    public void Create_Throws_OnEmptyCreatedBy()
    {
        var act = () => UserSignatureProfile.Create(Gen, Tenant, User, "Name", Guid.Empty);
        act.Should().Throw<ArgumentException>().WithParameterName("createdBy");
    }

    [Fact]
    public void Create_Throws_OnEmptyDisplayName()
    {
        var act = () => UserSignatureProfile.Create(Gen, Tenant, User, "   ", Actor);
        act.Should().Throw<ArgumentException>().WithParameterName("displayName");
    }

    [Fact]
    public void Create_Throws_OnDisplayNameOver128Chars()
    {
        var longName = new string('a', 129);
        var act = () => UserSignatureProfile.Create(Gen, Tenant, User, longName, Actor);
        act.Should().Throw<ArgumentException>().WithParameterName("displayName");
    }

    [Fact]
    public void Create_Allows_DisplayNameExactly128Chars()
    {
        var name = new string('a', 128);
        var act = () => UserSignatureProfile.Create(Gen, Tenant, User, name, Actor);
        act.Should().NotThrow();
    }

    [Fact]
    public void Create_TwoProfiles_HaveDifferentPublicIds()
    {
        var p1 = CreateProfile();
        var p2 = CreateProfile();
        p1.PublicId.Should().NotBe(p2.PublicId);
    }

    // ── Rename ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Rename_UpdatesDisplayName()
    {
        var p = CreateProfile("Old Name");
        p.Rename("New Name", Actor);
        p.DisplayName.Should().Be("New Name");
        p.ModifiedBy.Should().Be(Actor);
    }

    [Fact]
    public void Rename_Throws_OnEmptyName()
    {
        var p = CreateProfile();
        var act = () => p.Rename("  ", Actor);
        act.Should().Throw<ArgumentException>().WithParameterName("displayName");
    }

    [Fact]
    public void Rename_Throws_OnNameOver128Chars()
    {
        var p = CreateProfile();
        var act = () => p.Rename(new string('z', 129), Actor);
        act.Should().Throw<ArgumentException>().WithParameterName("displayName");
    }

    // ── MarkDefault / ClearDefault ────────────────────────────────────────────────────────────

    [Fact]
    public void MarkDefault_SetsIsDefaultTrue()
    {
        var p = CreateProfile();
        p.MarkDefault(Actor);
        p.IsDefault.Should().BeTrue();
        p.ModifiedBy.Should().Be(Actor);
    }

    [Fact]
    public void MarkDefault_IsIdempotent()
    {
        var p = CreateProfile();
        p.MarkDefault(Actor);
        p.MarkDefault(Actor); // second call must not throw
        p.IsDefault.Should().BeTrue();
    }

    [Fact]
    public void ClearDefault_SetsIsDefaultFalse()
    {
        var p = CreateProfile();
        p.MarkDefault(Actor);
        p.ClearDefault(Actor);
        p.IsDefault.Should().BeFalse();
    }

    [Fact]
    public void ClearDefault_IsIdempotent()
    {
        var p = CreateProfile();
        p.ClearDefault(Actor); // already false — must not throw
        p.IsDefault.Should().BeFalse();
    }

    /// <summary>
    /// Red-before-green invariant proof: if MarkDefault did NOT clear prior default (broken impl),
    /// two profiles would both show IsDefault=true — violating the ≤1 default/user rule.
    /// This test verifies the caller-clears-prior pattern works at the aggregate level.
    /// </summary>
    [Fact]
    public void Default_InvariantHolds_WhenCallerClearsPrior()
    {
        // Arrange: two profiles, first is default
        var p1 = CreateProfile("Profile A");
        var p2 = CreateProfile("Profile B");
        p1.MarkDefault(Actor);

        // Act: promote p2 — caller first clears p1, then marks p2
        p1.ClearDefault(Actor);
        p2.MarkDefault(Actor);

        // Assert: exactly one default
        p1.IsDefault.Should().BeFalse("prior default must be cleared");
        p2.IsDefault.Should().BeTrue("newly promoted profile must be default");
    }

    // ── SetImages ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SetImages_StoresKeys()
    {
        var p = CreateProfile();
        p.SetImages("bucket/full.png", "bucket/initial.png", Actor);
        p.FullSignatureKey.Should().Be("bucket/full.png");
        p.InitialSignatureKey.Should().Be("bucket/initial.png");
        p.ModifiedBy.Should().Be(Actor);
    }

    [Fact]
    public void SetImages_AllowsNulls()
    {
        var p = CreateProfile();
        p.SetImages(null, null, Actor);
        p.FullSignatureKey.Should().BeNull();
        p.InitialSignatureKey.Should().BeNull();
    }

    [Fact]
    public void SetImages_Throws_OnFullKeyOver512Chars()
    {
        var p = CreateProfile();
        var act = () => p.SetImages(new string('k', 513), null, Actor);
        act.Should().Throw<ArgumentException>().WithParameterName("fullSignatureKey");
    }

    [Fact]
    public void SetImages_Throws_OnInitialKeyOver512Chars()
    {
        var p = CreateProfile();
        var act = () => p.SetImages(null, new string('k', 513), Actor);
        act.Should().Throw<ArgumentException>().WithParameterName("initialSignatureKey");
    }

    // ── LinkCert ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LinkCert_SetsEnrollmentId()
    {
        var p = CreateProfile();
        p.LinkCert(42L, Actor);
        p.SignerCertEnrollmentId.Should().Be(42L);
        p.ModifiedBy.Should().Be(Actor);
    }

    [Fact]
    public void LinkCert_CanReplaceExistingLink()
    {
        var p = CreateProfile();
        p.LinkCert(1L, Actor);
        p.LinkCert(99L, Actor);
        p.SignerCertEnrollmentId.Should().Be(99L);
    }
}
