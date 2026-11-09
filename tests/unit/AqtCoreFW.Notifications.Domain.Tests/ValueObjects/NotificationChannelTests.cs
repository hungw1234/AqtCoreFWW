using AqtCoreFW.Notifications.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Notifications.Domain.Tests.ValueObjects;

/// <summary>
/// Tests for the <see cref="NotificationChannel"/> value object — the parse-with-validation
/// factory and value equality. From() is case-insensitive and rejects unknown channels;
/// both branches are pinned so a regression to the switch is caught.
/// </summary>
public sealed class NotificationChannelTests
{
    [Theory]
    [InlineData("email")]
    [InlineData("EMAIL")]
    [InlineData("Email")]
    public void From_KnownValue_IsCaseInsensitiveAndReturnsCanonicalSingleton(string raw)
    {
        NotificationChannel.From(raw).Should().BeSameAs(NotificationChannel.Email);
    }

    [Fact]
    public void From_EachKnownChannel_MapsToItsSingleton()
    {
        NotificationChannel.From("sms").Should().BeSameAs(NotificationChannel.Sms);
        NotificationChannel.From("inapp").Should().BeSameAs(NotificationChannel.InApp);
    }

    [Theory]
    [InlineData("fax")]
    [InlineData("")]
    [InlineData("e-mail")]
    public void From_UnknownValue_ThrowsArgumentException(string raw)
    {
        var act = () => NotificationChannel.From(raw);

        act.Should().Throw<ArgumentException>().WithParameterName("value");
    }

    [Fact]
    public void Equality_IsByValue_NotReference()
    {
        // ValueObject equality: parsed instance equals the canonical singleton.
        NotificationChannel.From("sms").Should().Be(NotificationChannel.Sms);
        NotificationChannel.Email.Should().NotBe(NotificationChannel.Sms);
    }

    [Fact]
    public void ToString_ReturnsUnderlyingValue()
    {
        NotificationChannel.InApp.ToString().Should().Be("inapp");
    }
}
