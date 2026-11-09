using System.Text.Json;
using AqtCoreFW.Files.Application.DTOs;
using AqtCoreFW.Files.Domain.Entities;

namespace AqtCoreFW.Tests.Contracts.Api;

/// <summary>
/// Contract tests for Files module DTOs — TC-CON-API-005.
/// </summary>
public sealed class FilesApiContractTests
{
    [Fact]
    [Trait("Category", "Contract")]
    public void FileMetadataDto_JsonShape_AllFieldsPresent()
    {
        var dto = new FileMetadataDto(
            Guid.NewGuid(), "report.pdf", "application/pdf", 1024_000,
            "abc123sha256", FileStatus.Available, Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(),
            DateTimeOffset.UtcNow, null);

        var json = JsonSerializer.Serialize(dto);
        var root = JsonDocument.Parse(json).RootElement;

        root.TryGetProperty("Id", out _).Should().BeTrue();
        root.TryGetProperty("OriginalFileName", out _).Should().BeTrue();
        root.TryGetProperty("ContentType", out _).Should().BeTrue();
        root.TryGetProperty("SizeBytes", out _).Should().BeTrue();
        root.TryGetProperty("ChecksumSha256", out _).Should().BeTrue();
        root.TryGetProperty("Status", out _).Should().BeTrue();
        root.TryGetProperty("UploadedBy", out _).Should().BeTrue();
        root.TryGetProperty("CreatedAt", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Contract")]
    public void FileMetadataDto_RoundTrip_Lossless()
    {
        var dto = new FileMetadataDto(
            Guid.NewGuid(), "doc.docx", "application/vnd.openxmlformats", 2048,
            "sha256hash", FileStatus.Available, Guid.NewGuid(),
            Guid.NewGuid(), null, DateTimeOffset.UtcNow, null);

        var json = JsonSerializer.Serialize(dto);
        var d = JsonSerializer.Deserialize<FileMetadataDto>(json);

        d.Should().NotBeNull();
        d!.Id.Should().Be(dto.Id);
        d.OriginalFileName.Should().Be(dto.OriginalFileName);
        d.ContentType.Should().Be(dto.ContentType);
        d.SizeBytes.Should().Be(dto.SizeBytes);
        d.ChecksumSha256.Should().Be(dto.ChecksumSha256);
        d.Status.Should().Be(dto.Status);
        d.TenantId.Should().Be(dto.TenantId);
        d.UploadedBy.Should().Be(dto.UploadedBy);
    }
}
