using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Signing;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Identity;

/// <summary>
/// Covers <see cref="VgcaCertSubjectParser.Classify"/> — the derivation that supplies the PAdES stamp's
/// "Ký bởi" line (the SIGNER'S name from the signing certificate subject, not the signature profile label).
/// Personal ⇒ CN; Organisation ⇒ O (fallback CN); empty subject ⇒ (Organization, "") so the caller can fall
/// back to the profile label.
/// </summary>
public sealed class VgcaCertSubjectParserClassifyTests
{
    [Fact]
    public void Personal_subject_yields_CN_as_display_name()
    {
        var (type, name) = VgcaCertSubjectParser.Classify("CN=Nguyễn Văn Đệ, SERIALNUMBER=CCCD:012345678912, C=VN");

        type.Should().Be(CertSubjectType.Personal);
        name.Should().Be("Nguyễn Văn Đệ");
    }

    [Fact]
    public void Organisation_subject_yields_O_as_display_name()
    {
        // No national-id RDN → Organisation; display name is the O value, not the CN.
        var (type, name) = VgcaCertSubjectParser.Classify("CN=Viettel-CA RS, O=Viettel Group, C=VN");

        type.Should().Be(CertSubjectType.Organization);
        name.Should().Be("Viettel Group");
    }

    [Fact]
    public void Organisation_without_O_falls_back_to_CN()
    {
        var (type, name) = VgcaCertSubjectParser.Classify("CN=Some Root CA, C=VN");

        type.Should().Be(CertSubjectType.Organization);
        name.Should().Be("Some Root CA");
    }

    [Fact]
    public void Blank_subject_yields_empty_name_so_the_caller_can_fall_back()
    {
        var (_, name) = VgcaCertSubjectParser.Classify("");

        name.Should().BeEmpty();
    }
}
