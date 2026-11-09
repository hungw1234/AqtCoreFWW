using AqtCoreFW.Identity.Application.Signing.Profiles;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Identity.Application.Tests.Signing.Profiles;

/// <summary>CCCD format rule — exactly 12 digits, matching the admin update path.</summary>
public sealed class SetMySigningNationalIdCommandValidatorTests
{
    private readonly SetMySigningNationalIdCommandValidator _sut = new();

    [Theory]
    [InlineData("079201001234")]   // 12 digits
    public void Valid12Digits_Passes(string cccd)
    {
        var r = _sut.Validate(new SetMySigningNationalIdCommand(Guid.NewGuid(), Guid.NewGuid(), cccd));
        r.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]               // empty
    [InlineData("07920100123")]    // 11 digits
    [InlineData("0792010012345")]  // 13 digits
    [InlineData("07920100123x")]   // non-digit
    [InlineData("079 201 001234")] // spaces
    public void InvalidFormat_Fails(string cccd)
    {
        var r = _sut.Validate(new SetMySigningNationalIdCommand(Guid.NewGuid(), Guid.NewGuid(), cccd));
        r.IsValid.Should().BeFalse();
    }
}
