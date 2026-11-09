using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Audit.Domain.Tests.Entities;

/// <summary>
/// Unit tests for LoginAttempt entity (T3 plain-class: long InternalId PK + Guid MessageId AK).
/// TC-AUD-D011: LoginAttempt records success/failure correctly.
/// </summary>
public sealed class LoginAttemptTests
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static IGuidGenerator NewGen()
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        return gen;
    }

    // --- TC-AUD-D011: Records success ---

    [Fact]
    public void Record_SuccessfulLogin_SetsSuccessTrue()
    {
        var attempt = LoginAttempt.Record(NewGen(), UserId, "user@gov.vn", "10.0.0.1", "Mozilla/5.0", true);

        attempt.Success.Should().BeTrue();
    }

    [Fact]
    public void Record_SuccessfulLogin_SetsAllFields()
    {
        var attempt = LoginAttempt.Record(NewGen(), UserId, "user@gov.vn", "192.168.1.100", "Chrome/120", true);

        attempt.UserId.Should().Be(UserId);
        attempt.Email.Should().Be("user@gov.vn");
        attempt.IpAddress.Should().Be("192.168.1.100");
        attempt.UserAgent.Should().Be("Chrome/120");
        attempt.Success.Should().BeTrue();
        attempt.FailureReason.Should().BeNull();
    }

    [Fact]
    public void Record_SuccessfulLogin_FailureReasonIsNull()
    {
        var attempt = LoginAttempt.Record(NewGen(), UserId, "user@gov.vn", "10.0.0.1", null, true);

        attempt.FailureReason.Should().BeNull();
    }

    // --- Records failure ---

    [Fact]
    public void Record_FailedLogin_SetsSuccessFalse()
    {
        var attempt = LoginAttempt.Record(NewGen(), null, "attacker@bad.com", "1.2.3.4", null,
            false, "Invalid password");

        attempt.Success.Should().BeFalse();
    }

    [Fact]
    public void Record_FailedLogin_SetsFailureReason()
    {
        const string reason = "Account locked after 5 attempts";
        var attempt = LoginAttempt.Record(NewGen(), null, "user@gov.vn", "1.2.3.4", null,
            false, reason);

        attempt.FailureReason.Should().Be(reason);
    }

    [Fact]
    public void Record_FailedLogin_WithNullUserId_UserIdIsNull()
    {
        var attempt = LoginAttempt.Record(NewGen(), null, "unknown@bad.com", "5.5.5.5", null,
            false, "User not found");

        attempt.UserId.Should().BeNull();
    }

    // --- Common properties ---

    [Fact]
    public void Record_GeneratesNonEmptyMessageId()
    {
        var attempt = LoginAttempt.Record(NewGen(), UserId, "u@gov.vn", "10.0.0.1", null, true);

        // T3: identity key is MessageId (Guid v7), not InternalId (DB-assigned long)
        attempt.MessageId.Should().NotBeEmpty();
    }

    [Fact]
    public void Record_TwoAttempts_HaveDifferentMessageIds()
    {
        var a = LoginAttempt.Record(NewGen(), UserId, "u@gov.vn", "10.0.0.1", null, true);
        var b = LoginAttempt.Record(NewGen(), UserId, "u@gov.vn", "10.0.0.2", null, false, "wrong pw");

        a.MessageId.Should().NotBe(b.MessageId);
    }

    [Fact]
    public void Record_AttemptedAt_IsApproximatelyUtcNow()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var attempt = LoginAttempt.Record(NewGen(), UserId, "u@gov.vn", "10.0.0.1", null, true);

        attempt.AttemptedAt.Should().BeAfter(before);
        attempt.AttemptedAt.Should().BeOnOrBefore(DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Fact]
    public void Record_WithUserAgent_StoresUserAgent()
    {
        const string ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";
        var attempt = LoginAttempt.Record(NewGen(), UserId, "u@gov.vn", "10.0.0.1", ua, true);

        attempt.UserAgent.Should().Be(ua);
    }

    [Fact]
    public void Record_WithNullUserAgent_UserAgentIsNull()
    {
        var attempt = LoginAttempt.Record(NewGen(), UserId, "u@gov.vn", "10.0.0.1", null, true);

        attempt.UserAgent.Should().BeNull();
    }
}
