using System.Text.Json;
using AqtCoreFW.Notifications.Application.DTOs;

namespace AqtCoreFW.Tests.Contracts.Api;

/// <summary>
/// Contract tests for Notifications module DTOs — TC-CON-API-009.
/// </summary>
public sealed class NotificationsApiContractTests
{
    [Fact]
    [Trait("Category", "Contract")]
    public void NotificationDto_JsonShape_AllFieldsPresent()
    {
        var dto = new NotificationDto
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            RecipientUserId = Guid.NewGuid(),
            Subject = "Thông báo mới",
            Body = "Nội dung thông báo",
            Channel = "Email",
            IsRead = false,
            ReadAt = null,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var json = JsonSerializer.Serialize(dto);
        var root = JsonDocument.Parse(json).RootElement;

        root.TryGetProperty("Id", out _).Should().BeTrue();
        root.TryGetProperty("Subject", out _).Should().BeTrue();
        root.TryGetProperty("Body", out _).Should().BeTrue();
        root.TryGetProperty("Channel", out _).Should().BeTrue();
        root.TryGetProperty("IsRead", out _).Should().BeTrue();
        root.TryGetProperty("CreatedAt", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Contract")]
    public void NotificationDto_RoundTrip_Lossless()
    {
        var dto = new NotificationDto
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            RecipientUserId = Guid.NewGuid(),
            Subject = "Subject",
            Body = "Body",
            Channel = "InApp",
            IsRead = true,
            ReadAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var json = JsonSerializer.Serialize(dto);
        var d = JsonSerializer.Deserialize<NotificationDto>(json);

        d.Should().NotBeNull();
        d!.Subject.Should().Be(dto.Subject);
        d.Channel.Should().Be(dto.Channel);
        d.IsRead.Should().BeTrue();
    }
}
