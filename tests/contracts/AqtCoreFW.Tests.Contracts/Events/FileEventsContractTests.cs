using System.Text.Json;
using AqtCoreFW.Files.Domain.Events;
using FluentAssertions;

namespace AqtCoreFW.Tests.Contracts.Events;

/// <summary>
/// Contract tests for Files module domain events — serialization round-trip stability.
/// Phase-4b/B3: FileId (VO struct) → Guid FilePublicId (T2 PublicId AK).
/// EventId → MessageId (IDomainEvent contract rename phase-04a/B1).
/// Phase-02c: MessageId is now positional ctor param (Rule1 compliant — no Guid.NewGuid() default).
/// </summary>
public sealed class FileEventsContractTests
{
    [Fact]
    [Trait("Category", "Contract")]
    public void FileUploadedEvent_RoundTrip()
    {
        var filePublicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var tenantId     = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var messageId    = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

        var evt = new FileUploadedEvent(
            filePublicId,
            tenantId,
            "document.pdf",
            1024 * 1024,
            messageId);

        var json = JsonSerializer.Serialize(evt);
        var d = JsonSerializer.Deserialize<FileUploadedEvent>(json);

        d.Should().NotBeNull();
        d!.FilePublicId.Should().Be(evt.FilePublicId);
        d.TenantId.Should().Be(evt.TenantId);
        d.OriginalFileName.Should().Be("document.pdf");
        d.SizeBytes.Should().Be(1024 * 1024);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public void FileQuarantinedEvent_RoundTrip()
    {
        var filePublicId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var tenantId     = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var messageId    = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

        var evt = new FileQuarantinedEvent(
            filePublicId,
            tenantId,
            "malware.exe",
            messageId);

        var json = JsonSerializer.Serialize(evt);
        var d = JsonSerializer.Deserialize<FileQuarantinedEvent>(json);

        d.Should().NotBeNull();
        d!.FilePublicId.Should().Be(evt.FilePublicId);
        d.OriginalFileName.Should().Be("malware.exe");
    }

    [Fact]
    [Trait("Category", "Contract")]
    public void FileUploadedEvent_JsonShape_ContainsExpectedProperties()
    {
        var evt = new FileUploadedEvent(Guid.NewGuid(), Guid.NewGuid(), "test.txt", 100, Guid.NewGuid());
        var json = JsonSerializer.Serialize(evt);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("FilePublicId", out _).Should().BeTrue();
        root.TryGetProperty("TenantId", out _).Should().BeTrue();
        root.TryGetProperty("OriginalFileName", out _).Should().BeTrue();
        root.TryGetProperty("SizeBytes", out _).Should().BeTrue();
        root.TryGetProperty("MessageId", out _).Should().BeTrue();
        root.TryGetProperty("OccurredAt", out _).Should().BeTrue();
    }
}
