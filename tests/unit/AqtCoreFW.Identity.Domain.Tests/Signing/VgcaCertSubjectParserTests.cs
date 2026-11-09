using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Signing;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Identity.Domain.Tests.Signing;

/// <summary>
/// Unit tests for <see cref="VgcaCertSubjectParser"/>:
/// - CCCD/CMND extraction (bootstrap binding path).
/// - Organisation-name extraction (O RDN, CN fallback, escaped-comma handling).
/// - Classify: Personal (has national-id) vs Organisation; display-name derivation.
/// </summary>
public sealed class VgcaCertSubjectParserTests
{
    [Theory]
    // SERIALNUMBER with CCCD: prefix (12 digits)
    [InlineData("CN=NGUYEN VAN A, SERIALNUMBER=CCCD:012345678901, C=VN", "012345678901")]
    // SERIALNUMBER with CMND: prefix (legacy 9 digits)
    [InlineData("CN=TRAN THI B, SERIALNUMBER=CMND:012345678, C=VN", "012345678")]
    // Bare SERIALNUMBER (no prefix)
    [InlineData("CN=LE VAN C, SERIALNUMBER=012345678901", "012345678901")]
    // Raw OID rendering of serialNumber
    [InlineData("CN=X, OID.2.5.4.5=CCCD:079200001234", "079200001234")]
    // UID fallback
    [InlineData("UID=001122334455, CN=Y", "001122334455")]
    // Whitespace tolerance around RDN
    [InlineData("CN=Z ,  SERIALNUMBER = 012345678901 ", "012345678901")]
    public void TryExtractNationalId_KnownFormats_ExtractsDigits(string subject, string expected)
    {
        VgcaCertSubjectParser.TryExtractNationalId(subject, out var nid).Should().BeTrue();
        nid.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("CN=No Id Here, O=Org, C=VN")]                       // no id RDN
    [InlineData("CN=Short, SERIALNUMBER=12345")]                      // too few digits (< 9)
    [InlineData("CN=NoEquals")]                                       // malformed RDN
    // Org tax code (MST) must NOT be read as a citizen id — otherwise an org cert would be misclassified
    // Personal and then hard-rejected on the USB enroll path (RT/S1).
    [InlineData("CN=Cong ty X, SERIALNUMBER=MST:0301234567, O=Cong ty X, C=VN")]  // MST prefix, 10 digits
    [InlineData("CN=Cong ty Y, SERIALNUMBER=0301234567, O=Cong ty Y")]            // bare 10-digit MST (length rule)
    [InlineData("CN=Cong ty Z, SERIALNUMBER=0301234567001, O=Cong ty Z")]         // 13-digit MST (branch suffix)
    public void TryExtractNationalId_NoPlausibleId_FailsClosed(string? subject)
    {
        VgcaCertSubjectParser.TryExtractNationalId(subject, out var nid).Should().BeFalse();
        nid.Should().BeEmpty();
    }

    [Theory]
    [InlineData("012345678901", "012345678901")]
    [InlineData("CCCD:012345678901", "012345678901")]
    [InlineData("  079200001234 ", "079200001234")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void NormalizeNationalId_StripsToDigitRun(string? input, string expected)
    {
        VgcaCertSubjectParser.NormalizeNationalId(input).Should().Be(expected);
    }

    // ── TryExtractOrganizationName ────────────────────────────────────────────────────────────

    [Fact]
    public void TryExtractOrganizationName_OPresent_ReturnsOValue()
    {
        const string dn = "CN=Test RSA3072, O=BAN CƠ YẾU CHÍNH PHỦ, C=VN";
        VgcaCertSubjectParser.TryExtractOrganizationName(dn, out var org).Should().BeTrue();
        org.Should().Be("BAN CƠ YẾU CHÍNH PHỦ");
    }

    [Fact]
    public void TryExtractOrganizationName_OAbsent_FallsBackToCn()
    {
        const string dn = "CN=NGUYEN VAN A, C=VN";
        VgcaCertSubjectParser.TryExtractOrganizationName(dn, out var org).Should().BeTrue();
        org.Should().Be("NGUYEN VAN A");
    }

    [Fact]
    public void TryExtractOrganizationName_OWithEscapedComma_ReturnsFullOrgName()
    {
        // .NET X500DistinguishedName escapes a literal comma inside an RDN value as \,
        // e.g. "Công ty ABC, JSC" is rendered as "O=Công ty ABC\, JSC".
        // The parser must re-join the escaped fragment to produce the full name.
        const string dn = @"CN=Something, O=Công ty ABC\, JSC, C=VN";
        VgcaCertSubjectParser.TryExtractOrganizationName(dn, out var org).Should().BeTrue();
        org.Should().Be("Công ty ABC, JSC");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryExtractOrganizationName_BlankInput_ReturnsFalseAndEmpty(string? dn)
    {
        VgcaCertSubjectParser.TryExtractOrganizationName(dn, out var org).Should().BeFalse();
        org.Should().BeEmpty();
    }

    // ── Classify ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Classify_OrgCert_WithOAndCn_ReturnsOrganizationAndOName()
    {
        // Spec case: CN=Test RSA3072, O=BAN CƠ YẾU CHÍNH PHỦ, C=VN ⇒ (Organization, "BAN CƠ YẾU CHÍNH PHỦ")
        const string dn = "CN=Test RSA3072, O=BAN CƠ YẾU CHÍNH PHỦ, C=VN";
        var (type, displayName) = VgcaCertSubjectParser.Classify(dn);
        type.Should().Be(CertSubjectType.Organization);
        displayName.Should().Be("BAN CƠ YẾU CHÍNH PHỦ");
    }

    [Fact]
    public void Classify_PersonalCert_WithUidCccd_ReturnsPersonalAndCn()
    {
        // Spec case: OID.0.9.2342.19200300.100.1.1=CCCD:017091004878, CN=ĐINH QUỐC VIỆT, L=PHÚ THỌ, C=VN
        //           ⇒ (Personal, "ĐINH QUỐC VIỆT")
        const string dn = "OID.0.9.2342.19200300.100.1.1=CCCD:017091004878, CN=ĐINH QUỐC VIỆT, L=PHÚ THỌ, C=VN";
        var (type, displayName) = VgcaCertSubjectParser.Classify(dn);
        type.Should().Be(CertSubjectType.Personal);
        displayName.Should().Be("ĐINH QUỐC VIỆT");
    }

    [Fact]
    public void Classify_OrgCert_WithEscapedCommaInO_ReturnsFullOrgName()
    {
        // O value contains escaped comma — must survive classification and return full name.
        const string dn = @"CN=Corp Cert, O=Công ty ABC\, JSC, C=VN";
        var (type, displayName) = VgcaCertSubjectParser.Classify(dn);
        type.Should().Be(CertSubjectType.Organization);
        displayName.Should().Be("Công ty ABC, JSC");
    }

    [Theory]
    // Org cert carrying its MST (tax code) in the serialNumber RDN must classify Organization, NOT Personal —
    // this is the exact case that would otherwise be hard-rejected as a national-id mismatch (S1).
    [InlineData("CN=Cong ty ABC, SERIALNUMBER=MST:0301234567, O=Cong ty ABC, C=VN", "Cong ty ABC")]
    [InlineData("CN=Cong ty XYZ, SERIALNUMBER=0301234567001, O=Cong ty XYZ, C=VN", "Cong ty XYZ")]
    public void Classify_OrgCert_WithMstInSerialNumber_ReturnsOrganization(string dn, string expectedName)
    {
        var (type, displayName) = VgcaCertSubjectParser.Classify(dn);
        type.Should().Be(CertSubjectType.Organization, "an MST is an org tax code, not a citizen id");
        displayName.Should().Be(expectedName);
    }

    [Fact]
    public void Classify_OrgCert_OAbsent_FallsBackToCn()
    {
        // No O RDN — falls back to CN for the display name.
        const string dn = "CN=NGUYEN VAN A, C=VN";
        var (type, displayName) = VgcaCertSubjectParser.Classify(dn);
        type.Should().Be(CertSubjectType.Organization, "no national-id RDN ⇒ Organisation");
        displayName.Should().Be("NGUYEN VAN A");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Classify_EmptyInput_ReturnsOrganizationWithEmptyName(string? dn)
    {
        var (type, displayName) = VgcaCertSubjectParser.Classify(dn);
        type.Should().Be(CertSubjectType.Organization, "fail-open: no evidence of national-id");
        displayName.Should().BeEmpty();
    }
}
