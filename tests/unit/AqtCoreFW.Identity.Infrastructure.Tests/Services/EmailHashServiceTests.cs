using AqtCoreFW.Identity.Infrastructure.Services;
using AqtCoreFW.Infrastructure.Configuration;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Services;

/// <summary>
/// Unit tests for <see cref="EmailHashService"/>.
/// No DB, no Vault — IPepperResolver is stubbed.
/// </summary>
public sealed class EmailHashServiceTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static readonly byte[] PepperA = new byte[32];   // 32 zero bytes for tenant A
    private static readonly byte[] PepperB = new byte[32];   // 32 one  bytes for tenant B

    static EmailHashServiceTests()
    {
        Array.Fill(PepperA, (byte)0x01);
        Array.Fill(PepperB, (byte)0x02);
    }

    private static EmailHashService BuildSut(Guid tenantId, byte[] pepper)
    {
        var resolver = Substitute.For<IPepperResolver>();
        resolver.GetPepper(tenantId).Returns(pepper);
        return new EmailHashService(resolver);
    }

    private static EmailHashService BuildSutMultiTenant()
    {
        var resolver = Substitute.For<IPepperResolver>();
        resolver.GetPepper(TenantA).Returns(PepperA);
        resolver.GetPepper(TenantB).Returns(PepperB);
        resolver.GetPepper(Guid.Empty).Returns(PepperA); // sentinel uses same as TenantA for test isolation
        return new EmailHashService(resolver);
    }

    // ── Happy path ─────────────────────────────────────────────────────────────

    [Fact]
    public void ComputeHash_Returns_NonEmpty_Base64_String()
    {
        var sut = BuildSut(TenantA, PepperA);

        var hash = sut.ComputeHash("user@example.com", TenantA);

        hash.Should().NotBeNullOrWhiteSpace();
        // HMAC-SHA-256 is 32 bytes → 44 chars in Base64 (with padding)
        Convert.FromBase64String(hash).Should().HaveCount(32);
    }

    [Fact]
    public void ComputeHash_Is_Deterministic_For_Same_Input()
    {
        var sut = BuildSut(TenantA, PepperA);

        var h1 = sut.ComputeHash("user@example.com", TenantA);
        var h2 = sut.ComputeHash("user@example.com", TenantA);

        h1.Should().Be(h2);
    }

    // ── Canonicalization ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("user@example.com")]
    [InlineData("USER@EXAMPLE.COM")]
    [InlineData("User@Example.Com")]
    [InlineData("  user@example.com  ")]
    [InlineData("  USER@EXAMPLE.COM  ")]
    public void ComputeHash_Is_Case_And_Whitespace_Insensitive(string email)
    {
        var sut = BuildSut(TenantA, PepperA);
        var baseline = sut.ComputeHash("user@example.com", TenantA);

        var hash = sut.ComputeHash(email, TenantA);

        hash.Should().Be(baseline, because: $"'{email}' should canonicalize identically to 'user@example.com'");
    }

    [Fact]
    public void Canonicalize_LowercasesAndTrims()
    {
        EmailHashService.Canonicalize("  User@Example.COM  ").Should().Be("user@example.com");
    }

    // ── Tenant isolation ───────────────────────────────────────────────────────

    [Fact]
    public void ComputeHash_DifferentTenants_ProduceDifferentHashes()
    {
        var sut = BuildSutMultiTenant();

        var hashA = sut.ComputeHash("user@example.com", TenantA);
        var hashB = sut.ComputeHash("user@example.com", TenantB);

        hashA.Should().NotBe(hashB, because: "different peppers must produce different hashes");
    }

    [Fact]
    public void ComputeHash_SameTenant_DifferentEmails_ProduceDifferentHashes()
    {
        var sut = BuildSut(TenantA, PepperA);

        var h1 = sut.ComputeHash("alice@example.com", TenantA);
        var h2 = sut.ComputeHash("bob@example.com", TenantA);

        h1.Should().NotBe(h2);
    }

    // ── Null-tenant sentinel ───────────────────────────────────────────────────

    [Fact]
    public void ComputeHash_NullableTenant_Null_UsesGuidEmpty()
    {
        var resolver = Substitute.For<IPepperResolver>();
        resolver.GetPepper(Guid.Empty).Returns(PepperA);
        var sut = new EmailHashService(resolver);

        // Should not throw; Guid.Empty sentinel resolved from IPepperResolver
        var hash = sut.ComputeHash("user@example.com", (Guid?)null);

        hash.Should().NotBeNullOrWhiteSpace();
        resolver.Received(1).GetPepper(Guid.Empty);
    }

    [Fact]
    public void ComputeHash_GuidEmpty_And_NullTenant_ProduceSameHash()
    {
        var resolver = Substitute.For<IPepperResolver>();
        resolver.GetPepper(Guid.Empty).Returns(PepperA);
        var sut = new EmailHashService(resolver);

        var h1 = sut.ComputeHash("user@example.com", Guid.Empty);
        var h2 = sut.ComputeHash("user@example.com", (Guid?)null);

        h1.Should().Be(h2);
    }

    // ── Guid.Empty vs tenant pepper invariant (P0-1 regression guard) ─────────

    [Fact]
    public void ComputeHash_GuidEmpty_DiffersFrom_NonEmptyTenant()
    {
        // Proves the perturbation invariant required by TestPepperResolver:
        // ComputeHash(email, Guid.Empty) must differ from ComputeHash(email, someTenantId)
        // whenever the peppers for those two tenantIds are different.
        //
        // If this test fails, it means GetPepper(Guid.Empty) == GetPepper(tenantId),
        // which means the fixture cannot detect a P0-1 regression where the write path
        // accidentally uses tenant pepper but the read path uses global pepper.
        var resolver = Substitute.For<IPepperResolver>();
        var globalPepper = new byte[32];
        var tenantPepper = new byte[32];
        Array.Fill(globalPepper, (byte)0xAB);  // same base as TestPepperResolver.FixedPepper
        Array.Fill(tenantPepper, (byte)0xAB);
        tenantPepper[0] ^= 0xFF; // perturb byte 0 — mirrors TestPepperResolver logic
        tenantPepper[1] ^= (byte)(TenantA.GetHashCode() & 0xFF);

        resolver.GetPepper(Guid.Empty).Returns(globalPepper);
        resolver.GetPepper(TenantA).Returns(tenantPepper);

        var sut = new EmailHashService(resolver);
        const string email = "user@example.com";

        var globalHash = sut.ComputeHash(email, Guid.Empty);
        var tenantHash = sut.ComputeHash(email, TenantA);

        globalHash.Should().NotBe(tenantHash,
            because: "perturbed tenant pepper must produce a different hash from the global (Guid.Empty) pepper — " +
                     "this invariant ensures TestPepperResolver would catch a P0-1 write/read mismatch");
    }

    // ── Error handling ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ComputeHash_NullOrWhitespaceEmail_Throws(string? email)
    {
        var sut = BuildSut(TenantA, PepperA);

        var act = () => sut.ComputeHash(email!, TenantA);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ComputeHash_VaultMiss_PropagatesKeyNotFoundException()
    {
        var resolver = Substitute.For<IPepperResolver>();
        resolver.GetPepper(Arg.Any<Guid>())
            .Returns(_ => throw new KeyNotFoundException("No pepper in Vault"));

        var sut = new EmailHashService(resolver);

        var act = () => sut.ComputeHash("user@example.com", TenantA);

        act.Should().Throw<KeyNotFoundException>().WithMessage("*No pepper in Vault*");
    }
}
