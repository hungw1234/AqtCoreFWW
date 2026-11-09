using AqtCoreFW.AuthServer.Services;
using AqtCoreFW.Identity.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using NSubstitute;

namespace AqtCoreFW.AuthServer.Tests.Services;

/// <summary>
/// OWASP A07 / QĐ742 timing-decoy contract.
/// Asserts every Consume call invokes the password hasher's verify routine
/// with the supplied password, equalizing CPU cost vs the user-found path.
/// Backlog: BE-LOGIN-TIMING-SIDE-CHANNEL.
/// </summary>
public sealed class PasswordVerifyTimingDecoyTests
{
    private readonly IPasswordHasher<ApplicationUser> _hasher = Substitute.For<IPasswordHasher<ApplicationUser>>();

    [Fact]
    public void Consume_InvokesVerifyHashedPassword_WithSuppliedPassword()
    {
        var sut = new PasswordVerifyTimingDecoy(_hasher);

        sut.Consume("SuperSecret1!");

        _hasher.Received(1).VerifyHashedPassword(
            Arg.Any<ApplicationUser>(),
            Arg.Any<string>(),
            "SuperSecret1!");
    }

    [Fact]
    public void Consume_NullPassword_PassesEmptyStringToVerify()
    {
        var sut = new PasswordVerifyTimingDecoy(_hasher);

        sut.Consume(null!);

        _hasher.Received(1).VerifyHashedPassword(
            Arg.Any<ApplicationUser>(),
            Arg.Any<string>(),
            string.Empty);
    }

    [Fact]
    public void Consume_MultipleCalls_AlwaysInvokesVerify()
    {
        // Decoy hash is cached process-wide (static Lazy) but verify must
        // run on every call — that is the whole point of the timing parity.
        var sut = new PasswordVerifyTimingDecoy(_hasher);

        sut.Consume("first");
        sut.Consume("second");
        sut.Consume("third");

        _hasher.Received(3).VerifyHashedPassword(
            Arg.Any<ApplicationUser>(),
            Arg.Any<string>(),
            Arg.Any<string>());
    }

    [Fact]
    public void Consume_PassesNonEmptyDecoyHash_ToVerify()
    {
        var sut = new PasswordVerifyTimingDecoy(_hasher);

        sut.Consume("anything");

        _hasher.Received(1).VerifyHashedPassword(
            Arg.Any<ApplicationUser>(),
            Arg.Is<string>(h => !string.IsNullOrWhiteSpace(h)),
            "anything");
    }

    [Fact]
    public void Consume_WithRealPasswordHasher_DoesNotThrow_AndDecoyHashIsParseable()
    {
        // Guards the static-vs-DI hasher asymmetry: decoy hash is computed by
        // a standalone PasswordHasher (defaults), then passed to whichever
        // hasher the DI container hands the controller. If the cached decoy
        // string ever becomes unparseable, VerifyHashedPassword will throw —
        // the mock-based tests cannot catch that.
        var realHasher = new PasswordHasher<ApplicationUser>();
        var sut = new PasswordVerifyTimingDecoy(realHasher);

        var act = () => sut.Consume("AnyUserSuppliedPassword");

        act.Should().NotThrow();
    }
}
