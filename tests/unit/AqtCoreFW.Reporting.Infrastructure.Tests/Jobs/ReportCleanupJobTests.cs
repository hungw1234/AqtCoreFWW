using AqtCoreFW.Reporting.Domain.Entities;
using AqtCoreFW.Reporting.Domain.Repositories;
using AqtCoreFW.Reporting.Infrastructure.Jobs;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AqtCoreFW.Reporting.Infrastructure.Tests.Jobs;

/// <summary>
/// TC-RPT-I004: ReportCleanupJob deletes expired report execution files.
/// </summary>
public sealed class ReportCleanupJobTests
{
    // IGuidGenerator stub — ReportExecution.Create now requires it (T2 entity)
    private static readonly IGuidGenerator GuidGen = Substitute.For<IGuidGenerator>();

    static ReportCleanupJobTests()
    {
        GuidGen.NewIdMonotonic().Returns(Guid.Parse("55555555-5555-7555-8555-555555555555"));
    }
    [Fact]
    public async Task ExecuteAsync_NoExpiredExecutions_DoesNotCallSaveChanges()
    {
        // TC-RPT-I004: no expired → repo returns empty list → no saves
        var repo = Substitute.For<IReportExecutionRepository>();
        repo.GetExpiredAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ReportExecution>>([]));

        var sut = new ReportCleanupJob(repo, NullLogger<ReportCleanupJob>.Instance);

        await sut.ExecuteAsync();

        await repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ExpiredWithFilePath_DeletesFileAndCallsSaveChanges()
    {
        // TC-RPT-I004: expired execution with a real file → file deleted, ClearResultFile, SaveChanges
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "report data");

        var execution = ReportExecution.Create(GuidGen, 1L, "{}", Guid.NewGuid());
        execution.MarkDone(tempFile); // sets ResultFilePath

        var repo = Substitute.For<IReportExecutionRepository>();
        repo.GetExpiredAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ReportExecution>>([execution]));

        var sut = new ReportCleanupJob(repo, NullLogger<ReportCleanupJob>.Instance);

        await sut.ExecuteAsync();

        File.Exists(tempFile).Should().BeFalse();
        execution.ResultFilePath.Should().BeNullOrEmpty();
        await repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ExpiredWithNullFilePath_SkipsDeleteAndStillSaves()
    {
        // TC-RPT-I004: null ResultFilePath → skip file delete, still call SaveChanges
        var execution = ReportExecution.Create(GuidGen, 1L, "{}", Guid.NewGuid());
        // ResultFilePath is null by default (not marked Done)

        var repo = Substitute.For<IReportExecutionRepository>();
        repo.GetExpiredAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ReportExecution>>([execution]));

        var sut = new ReportCleanupJob(repo, NullLogger<ReportCleanupJob>.Instance);

        await sut.ExecuteAsync();

        // SaveChanges still called even when no file to delete
        await repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_NonExistentFile_DoesNotThrow()
    {
        // File already gone → graceful, no exception
        var execution = ReportExecution.Create(GuidGen, 1L, "{}", Guid.NewGuid());
        execution.MarkDone("/nonexistent/path/to/report.xlsx");

        var repo = Substitute.For<IReportExecutionRepository>();
        repo.GetExpiredAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ReportExecution>>([execution]));

        var sut = new ReportCleanupJob(repo, NullLogger<ReportCleanupJob>.Instance);

        var act = async () => await sut.ExecuteAsync();

        await act.Should().NotThrowAsync();
    }
}
