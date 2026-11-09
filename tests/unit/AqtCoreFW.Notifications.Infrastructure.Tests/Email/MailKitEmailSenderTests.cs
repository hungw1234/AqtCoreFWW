using AqtCoreFW.Notifications.Infrastructure.Email;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Notifications.Infrastructure.Tests.Email;

/// <summary>
/// TC-NOT-I005: MailKitEmailSender builds correct MIME message structure.
/// We test the options binding and that Connect failures propagate cleanly
/// (actual SMTP connection requires a real server — not suitable for unit tests).
/// </summary>
public sealed class MailKitEmailSenderTests
{
    [Fact]
    public void SmtpOptions_DefaultValues_AreCorrect()
    {
        // Verify the default SMTP configuration is sane (port 1025 = dev MailHog)
        var opts = new SmtpOptions();

        opts.Host.Should().Be("localhost");
        opts.Port.Should().Be(1025);
        opts.From.Should().Be("no-reply@gov.vn");
        opts.UseSsl.Should().BeFalse();
        opts.Username.Should().BeNull();
        opts.Password.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_UnreachableHost_ThrowsException()
    {
        // TC-NOT-I005: when SMTP host is unreachable, exception propagates (no silent swallow)
        var opts = Options.Create(new SmtpOptions
        {
            Host = "127.0.0.1",
            Port = 19999, // no server listening
            From = "no-reply@gov.vn",
            UseSsl = false
        });

        var sut = new MailKitEmailSender(opts, NullLogger<MailKitEmailSender>.Instance);

        var act = async () => await sut.SendAsync(
            "recipient@test.vn", "Subject", "<p>Body</p>");

        // Exception type varies (SocketException, MailKit.Net.Smtp.SmtpCommandException, etc.)
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public void SmtpOptions_SectionName_IsEmail()
    {
        SmtpOptions.SectionName.Should().Be("Email");
    }
}
