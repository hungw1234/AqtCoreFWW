using AqtCoreFW.Files.Application.Options;
using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.Files.Domain.Repositories;
using AqtCoreFW.Files.Domain.Services;
using AqtCoreFW.Files.Infrastructure.Jobs;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Contracts;
using FluentAssertions;
using FluentResults;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Files.Infrastructure.Tests.Jobs;

/// <summary>
/// Unit tests for ClamAvScanJob — the async phase-2 verdict handler of the two-phase upload.
/// Pins the three-way branch the job MUST make on a scan result:
///   Clean    → Available
///   Infected → Rejected + deleted from storage (real malware)
///   Error    → throw (Hangfire retry), file stays Quarantined, storage untouched (infra fault, NOT a verdict)
/// The Error case is the critical one: the scanner is fail-closed, so an unreachable clamd yields Error,
/// and treating that as a rejection would destroy a possibly-clean file on a transient outage.
/// </summary>
public sealed class ClamAvScanJobTests
{
    private const string Bucket = "test-bucket";
    private const string ScanFlag = "feature:files.virus-scan";

    private readonly IFileRepository _fileRepository = Substitute.For<IFileRepository>();
    private readonly IFileStorageService _storage = Substitute.For<IFileStorageService>();
    private readonly IVirusScanner _scanner = Substitute.For<IVirusScanner>();
    private readonly IFeatureFlagService _featureFlags = Substitute.For<IFeatureFlagService>();
    private readonly IGuidGenerator _guid = Substitute.For<IGuidGenerator>();
    private readonly ClamAvScanJob _job;

    public ClamAvScanJobTests()
    {
        // Fully-qualified: `AqtCoreFW.Files.Infrastructure.Options` (ClamAvOptions' namespace) is in scope
        // here and would otherwise shadow Microsoft.Extensions.Options.Options.
        var options = Microsoft.Extensions.Options.Options.Create(new FilesOptions { BucketName = Bucket });
        _job = new ClamAvScanJob(
            _fileRepository, _storage, _scanner, _featureFlags, _guid, options,
            Substitute.For<ILogger<ClamAvScanJob>>());

        // Default: scan feature on (the dev/prod posture) + storage returns a non-empty stream.
        _featureFlags.IsEnabled(ScanFlag).Returns(true);
        _storage.DownloadAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(_ => Task.FromResult<Stream>(new MemoryStream(new byte[] { 1, 2, 3 })));
    }

    private FileRecord ArrangeQuarantinedFile()
    {
        _guid.NewIdMonotonic().Returns(Guid.NewGuid());
        var file = FileRecord.Create(
            _guid, Guid.NewGuid(), "doc.pdf", "files/doc.pdf", "application/pdf",
            sizeBytes: 3, checksumSha256: "sha", uploadedBy: Guid.NewGuid());
        _fileRepository.GetByIdAsync(Arg.Any<FileId>()).Returns(Result.Ok(file));
        return file;
    }

    [Fact]
    public async Task ScanAsync_Clean_MarksAvailable_AndKeepsObject()
    {
        var file = ArrangeQuarantinedFile();
        _scanner.ScanAsync(Arg.Any<Stream>()).Returns(VirusScanResult.Clean());

        await _job.ScanAsync(file.PublicId);

        file.Status.Should().Be(FileStatus.Available);
        await _storage.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ScanAsync_Infected_MarksRejected_AndDeletesObject()
    {
        var file = ArrangeQuarantinedFile();
        _scanner.ScanAsync(Arg.Any<Stream>()).Returns(VirusScanResult.Infected("Eicar-Test-Signature"));

        await _job.ScanAsync(file.PublicId);

        file.Status.Should().Be(FileStatus.Rejected);
        await _storage.Received(1).DeleteAsync(Bucket, file.StorageKey);
    }

    [Fact]
    public async Task ScanAsync_Error_Throws_AndPreservesQuarantineAndObject()
    {
        // Error = clamd unreachable / timeout / protocol fault. It is NOT a verdict, so the file must not be
        // destroyed: the job throws (→ Hangfire retry), the record stays Quarantined, storage is untouched.
        var file = ArrangeQuarantinedFile();
        _scanner.ScanAsync(Arg.Any<Stream>()).Returns(VirusScanResult.Error("clamd unreachable"));

        var act = async () => await _job.ScanAsync(file.PublicId);

        await act.Should().ThrowAsync<InvalidOperationException>();
        file.Status.Should().Be(FileStatus.Quarantined);
        await _storage.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<string>());
        await _fileRepository.DidNotReceive().UpdateAsync(Arg.Any<FileRecord>());
    }

    [Fact]
    public async Task ScanAsync_FeatureFlagOff_MarksAvailableWithoutScanning()
    {
        // Bypass branch: when the flag is off the job auto-passes (dev/demo) and never contacts the scanner.
        var file = ArrangeQuarantinedFile();
        _featureFlags.IsEnabled(ScanFlag).Returns(false);

        await _job.ScanAsync(file.PublicId);

        file.Status.Should().Be(FileStatus.Available);
        await _scanner.DidNotReceive().ScanAsync(Arg.Any<Stream>());
    }

    [Fact]
    public async Task ScanAsync_ReadsTheGlobalFlag_NeverATenantScopedOne()
    {
        // Malware scanning is deployment-wide posture: a tenant must not be able to opt its uploads out.
        // Reading tenant-scoped would arm exactly that, and would also be dead in production — the flag
        // cache is filled by a background service with no tenant, so RLS admits only NULL-tenant rows and
        // a tenant override would never be visible. Asserting the null tenant argument pins both.
        // Flag off so the job returns at the gate: this asserts HOW the flag is read, and routing it
        // through the scan path would only add a scanner stub the assertion does not need.
        var file = ArrangeQuarantinedFile();
        _featureFlags.IsEnabled(ScanFlag).Returns(false);

        await _job.ScanAsync(file.PublicId);

        _featureFlags.Received(1).IsEnabled(ScanFlag, null);
        _featureFlags.DidNotReceive().IsEnabled(ScanFlag, Arg.Is<string?>(t => t != null));
    }
}
