using System.Text.Json;
using AqtCoreFW.Identity.Domain.Events;

namespace AqtCoreFW.Tests.Contracts.Events;

/// <summary>
/// Contract tests for Identity module domain events — TC-CON-EVT-003, TC-CON-EVT-004.
/// </summary>
public sealed class IdentityDomainEventsContractTests
{
    [Fact]
    [Trait("Category", "Contract")]
    public void UserCreatedEvent_RoundTrip()
    {
        var evt = new UserCreatedEvent(
            Guid.NewGuid(), "Nguyen Van A", "a@gov.vn", Guid.NewGuid(), Guid.NewGuid());

        var json = JsonSerializer.Serialize(evt);
        var d = JsonSerializer.Deserialize<UserCreatedEvent>(json);

        d.Should().NotBeNull();
        d!.UserId.Should().Be(evt.UserId);
        d.FullName.Should().Be(evt.FullName);
        d.Email.Should().Be(evt.Email);
        d.TenantId.Should().Be(evt.TenantId);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public void UserCreatedEvent_JsonShape_ContainsExpectedProperties()
    {
        var evt = new UserCreatedEvent(Guid.NewGuid(), "Test", "t@gov.vn", null, Guid.NewGuid());
        var json = JsonSerializer.Serialize(evt);
        var root = JsonDocument.Parse(json).RootElement;

        root.TryGetProperty("UserId", out _).Should().BeTrue();
        root.TryGetProperty("FullName", out _).Should().BeTrue();
        root.TryGetProperty("Email", out _).Should().BeTrue();
        root.TryGetProperty("TenantId", out _).Should().BeTrue();
        root.TryGetProperty("MessageId", out _).Should().BeTrue();
        root.TryGetProperty("OccurredAt", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Contract")]
    public void UserLockedEvent_RoundTrip()
    {
        var evt = new UserLockedEvent(Guid.NewGuid(), true, Guid.NewGuid(), Guid.NewGuid());

        var json = JsonSerializer.Serialize(evt);
        var d = JsonSerializer.Deserialize<UserLockedEvent>(json);

        d.Should().NotBeNull();
        d!.UserId.Should().Be(evt.UserId);
        d.IsLocked.Should().Be(evt.IsLocked);
        d.LockedBy.Should().Be(evt.LockedBy);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Category", "Contract")]
    public void UserLockedEvent_IsLockedField_SurvivesRoundTrip(bool isLocked)
    {
        var evt = new UserLockedEvent(Guid.NewGuid(), isLocked, Guid.NewGuid(), Guid.NewGuid());

        var json = JsonSerializer.Serialize(evt);
        var d = JsonSerializer.Deserialize<UserLockedEvent>(json);

        d!.IsLocked.Should().Be(isLocked);
    }
}
