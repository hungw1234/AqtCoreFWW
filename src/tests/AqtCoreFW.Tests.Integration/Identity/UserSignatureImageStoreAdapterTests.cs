using AqtCoreFW.Api.CrossModule;
using AqtCoreFW.Files.Domain.Services;
using AqtCoreFW.Files.Infrastructure.Security;
using AqtCoreFW.SharedKernel.Contracts;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Identity;

/// <summary>
/// Validation teeth + store wiring for the host-resident <c>UserSignatureImageStoreAdapter</c> (cross-module
/// Identity→Files port). Uses the REAL <see cref="FileSecurityService"/> (extension blocklist) + an in-memory
/// storage fake — no MinIO needed. Proves: PNG/JPEG accepted with a tenant/user-scoped key + detected
/// content-type + byte round-trip; SVG rejected by extension; renamed-SVG / PDF-as-png rejected by the
/// magic-byte narrowing (declared content-type never trusted — S8); oversize is enforced by the command
/// validator (covered in the Application unit tests).
/// </summary>
public sealed class UserSignatureImageStoreAdapterTests
{
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02];
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46];
    private static readonly byte[] SvgBytes = "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray();
    private static readonly byte[] PdfBytes = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37]; // %PDF-1.7

    private sealed class InMemoryFileStorage : IFileStorageService
    {
        public readonly List<(string Bucket, string Key, string ContentType, byte[] Bytes)> Stored = [];

        public async Task<string> UploadAsync(Stream stream, string bucket, string key, string contentType, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            Stored.Add((bucket, key, contentType, ms.ToArray()));
            return key;
        }

        public Task<Stream> DownloadAsync(string bucket, string key, CancellationToken ct = default)
        {
            var found = Stored.First(s => s.Bucket == bucket && s.Key == key);
            return Task.FromResult<Stream>(new MemoryStream(found.Bytes));
        }

        public Task DeleteAsync(string bucket, string key, CancellationToken ct = default)
        {
            Stored.RemoveAll(s => s.Bucket == bucket && s.Key == key);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<StorageObjectInfo>> ListObjectsAsync(string bucket, string prefix, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<StorageObjectInfo>>([]);
    }

    /// <summary>Virus scanner stub — returns a fixed verdict (Clean by default) without touching clamd.</summary>
    private sealed class StubVirusScanner(VirusScanResult result) : IVirusScanner
    {
        public Task<VirusScanResult> ScanAsync(Stream stream, CancellationToken ct = default)
            => Task.FromResult(result);
    }

    /// <summary>Feature-flag stub — the virus-scan flag is on by default so tests exercise the scan gate.</summary>
    private sealed class StubFeatureFlags(bool enabled) : IFeatureFlagService
    {
        public bool IsEnabled(string key, string? tenantId = null) => enabled;
    }

    private static (UserSignatureImageStoreAdapter Adapter, InMemoryFileStorage Storage) Build(
        VirusScanResult? scanVerdict = null,
        bool virusScanEnabled = true)
    {
        var storage = new InMemoryFileStorage();
        var security = new FileSecurityService(NullLogger<FileSecurityService>.Instance);
        var scanner = new StubVirusScanner(scanVerdict ?? VirusScanResult.Clean());
        var flags = new StubFeatureFlags(virusScanEnabled);
        var adapter = new UserSignatureImageStoreAdapter(
            security, storage, scanner, flags, NullLogger<UserSignatureImageStoreAdapter>.Instance);
        return (adapter, storage);
    }

    [Fact]
    public async Task ValidPng_StoresScopedKey_DetectedContentType_RoundTrips()
    {
        var (adapter, storage) = Build();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var result = await adapter.ValidateAndStoreAsync(tenantId, userId, PngBytes, "signature.png");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().StartWith($"{tenantId}/{userId}/").And.EndWith(".png");

        var stored = storage.Stored.Should().ContainSingle().Subject;
        stored.Bucket.Should().Be("user-signatures");
        stored.Key.Should().Be(result.Value);
        stored.ContentType.Should().Be("image/png", "the stored type is derived from the bytes, not the client");
        stored.Bytes.Should().Equal(PngBytes, "the stored bytes round-trip unchanged");
    }

    [Fact]
    public async Task ValidJpeg_StoresJpgKey()
    {
        var (adapter, storage) = Build();
        var result = await adapter.ValidateAndStoreAsync(Guid.NewGuid(), Guid.NewGuid(), JpegBytes, "sig.jpg");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().EndWith(".jpg");
        storage.Stored.Single().ContentType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task SvgExtension_RejectedByBlocklist_NothingStored()
    {
        var (adapter, storage) = Build();
        var result = await adapter.ValidateAndStoreAsync(Guid.NewGuid(), Guid.NewGuid(), SvgBytes, "evil.svg");

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ValidationError);
        storage.Stored.Should().BeEmpty();
    }

    [Fact]
    public async Task RenamedSvg_RejectedByMagicByteNarrowing_NothingStored()
    {
        // .png extension passes the blocklist, but the bytes are SVG → the magic-byte narrowing rejects it.
        var (adapter, storage) = Build();
        var result = await adapter.ValidateAndStoreAsync(Guid.NewGuid(), Guid.NewGuid(), SvgBytes, "evil.png");

        result.IsFailed.Should().BeTrue();
        storage.Stored.Should().BeEmpty();
    }

    [Fact]
    public async Task PdfDeclaredAsPng_RejectedByMagicByteNarrowing()
    {
        // The Files allowlist accepts PDF, but a signature image must be PNG/JPEG — magic-byte mismatch rejects.
        var (adapter, _) = Build();
        var result = await adapter.ValidateAndStoreAsync(Guid.NewGuid(), Guid.NewGuid(), PdfBytes, "doc.png");

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task TooShortBytes_Rejected()
    {
        var (adapter, _) = Build();
        var result = await adapter.ValidateAndStoreAsync(Guid.NewGuid(), Guid.NewGuid(), [0x89], "tiny.png");

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_RemovesStoredBlob_FromUserSignaturesBucket()
    {
        var (adapter, storage) = Build();
        var key = (await adapter.ValidateAndStoreAsync(Guid.NewGuid(), Guid.NewGuid(), PngBytes, "sig.png")).Value;
        storage.Stored.Should().ContainSingle();

        await adapter.DeleteAsync(key);

        storage.Stored.Should().BeEmpty("the prior slot blob is reclaimed on overwrite (M2)");
    }

    [Fact]
    public async Task DeleteAsync_SwallowsStorageErrors()
    {
        // A failed reclaim must never surface — it would otherwise fail an upload that already committed.
        var adapter = new UserSignatureImageStoreAdapter(
            new FileSecurityService(NullLogger<FileSecurityService>.Instance),
            new ThrowingFileStorage(),
            new StubVirusScanner(VirusScanResult.Clean()),
            new StubFeatureFlags(enabled: true),
            NullLogger<UserSignatureImageStoreAdapter>.Instance);

        var act = async () => await adapter.DeleteAsync("any/key.png");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task InfectedImage_Rejected_NothingStored()
    {
        // A real PNG magic-byte that ClamAV flags as infected → 422 rejection, nothing persisted.
        var (adapter, storage) = Build(scanVerdict: VirusScanResult.Infected("Eicar-Test-Signature"));

        var result = await adapter.ValidateAndStoreAsync(Guid.NewGuid(), Guid.NewGuid(), PngBytes, "sig.png");

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ValidationError);
        storage.Stored.Should().BeEmpty("an infected image must never reach storage");
    }

    [Fact]
    public async Task ScannerUnreachable_FailsClosed_NothingStored()
    {
        // clamd down (Error verdict) with scanning enabled → fail-closed 503, image not persisted.
        var (adapter, storage) = Build(scanVerdict: VirusScanResult.Error("clamd unreachable"));

        var result = await adapter.ValidateAndStoreAsync(Guid.NewGuid(), Guid.NewGuid(), PngBytes, "sig.png");

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ServiceUnavailableError);
        storage.Stored.Should().BeEmpty("an unscanned image must never reach storage when scanning is enabled");
    }

    [Fact]
    public async Task ScannerThrows_FailsClosed_NothingStored()
    {
        // A scanner implementation that throws (not the fail-closed ClamAvVirusScanner) must still degrade to
        // a retryable 503 and never persist an unscanned image — the contract-boundary hardening (N1).
        var storage = new InMemoryFileStorage();
        var adapter = new UserSignatureImageStoreAdapter(
            new FileSecurityService(NullLogger<FileSecurityService>.Instance),
            storage,
            new ThrowingVirusScanner(),
            new StubFeatureFlags(enabled: true),
            NullLogger<UserSignatureImageStoreAdapter>.Instance);

        var result = await adapter.ValidateAndStoreAsync(Guid.NewGuid(), Guid.NewGuid(), PngBytes, "sig.png");

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ServiceUnavailableError);
        storage.Stored.Should().BeEmpty();
    }

    private sealed class ThrowingVirusScanner : IVirusScanner
    {
        public Task<VirusScanResult> ScanAsync(Stream stream, CancellationToken ct = default)
            => throw new InvalidOperationException("scanner backend down");
    }

    [Fact]
    public async Task VirusScanDisabled_SkipsScan_StoresWithoutScanning()
    {
        // Dev/demo bypass: flag off → the (would-be-Error) scanner is never consulted, upload proceeds.
        var (adapter, storage) = Build(
            scanVerdict: VirusScanResult.Error("clamd unreachable"), virusScanEnabled: false);

        var result = await adapter.ValidateAndStoreAsync(Guid.NewGuid(), Guid.NewGuid(), PngBytes, "sig.png");

        result.IsSuccess.Should().BeTrue("with scanning disabled the dev flow must not be blocked");
        storage.Stored.Should().ContainSingle();
    }

    private sealed class ThrowingFileStorage : IFileStorageService
    {
        public Task<string> UploadAsync(Stream s, string b, string k, string c, CancellationToken ct = default) => throw new InvalidOperationException();
        public Task<Stream> DownloadAsync(string b, string k, CancellationToken ct = default) => throw new InvalidOperationException();
        public Task DeleteAsync(string b, string k, CancellationToken ct = default) => throw new InvalidOperationException("backend down");
        public Task<IReadOnlyList<StorageObjectInfo>> ListObjectsAsync(string b, string p, CancellationToken ct = default) => throw new InvalidOperationException();
    }
}
