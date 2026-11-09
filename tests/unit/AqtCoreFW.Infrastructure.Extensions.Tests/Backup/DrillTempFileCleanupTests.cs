using AqtCoreFW.Infrastructure.Backup;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Infrastructure.Extensions.Tests.Backup;

/// <summary>
/// Unit tests for <see cref="DrillTempFileCleanup.TryDelete"/> — the swallow-and-log temp-file
/// cleanup a restore drill runs in its <c>finally</c>. Exercises the real filesystem via throwaway
/// files under <see cref="Path.GetTempPath"/>; each test cleans up after itself.
/// </summary>
public sealed class DrillTempFileCleanupTests
{
    private static string NewTempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drill-cleanup-test-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void TryDelete_ExistingFiles_AllDeleted()
    {
        var files = new[] { NewTempFile(), NewTempFile(), NewTempFile() };

        try
        {
            DrillTempFileCleanup.TryDelete(files, NullLogger.Instance);

            files.Should().OnlyContain(p => !File.Exists(p));
        }
        finally
        {
            foreach (var f in files.Where(File.Exists))
            {
                File.Delete(f);
            }
        }
    }

    [Fact]
    public void TryDelete_MissingFile_NoThrow_DeletesTheRealOne()
    {
        var real = NewTempFile();
        var missing = Path.Combine(Path.GetTempPath(), $"drill-cleanup-missing-{Guid.NewGuid():N}.tmp");

        try
        {
            var act = () => DrillTempFileCleanup.TryDelete(new[] { missing, real }, NullLogger.Instance);

            act.Should().NotThrow();
            File.Exists(real).Should().BeFalse();
        }
        finally
        {
            if (File.Exists(real))
            {
                File.Delete(real);
            }
        }
    }

    [Fact]
    public void TryDelete_NullOrEmptyPath_Skipped_NoThrow()
    {
        var act = () => DrillTempFileCleanup.TryDelete(new string?[] { null, "" }, NullLogger.Instance);

        act.Should().NotThrow();
    }

    [Fact]
    public void TryDelete_UndeletableFile_LogsWarning_NoThrow()
    {
        var logger = Substitute.For<ILogger>();

        // No single trigger fails a delete on both OSes: an exclusive file lock only blocks delete on
        // Windows (POSIX unlinks an open file), while a read-only parent dir only blocks it on Linux
        // (Windows delete permission is a file ACL, not the parent write-bit). Use the right trigger
        // per platform; both drive the same swallow-and-log invariant, so it is asserted on the Linux
        // CI runner too — not skipped.
        string target;
        FileStream? lockHandle = null;
        string? readOnlyDir = null;

        if (OperatingSystem.IsWindows())
        {
            target = NewTempFile();
            lockHandle = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None);
        }
        else
        {
            readOnlyDir = Path.Combine(Path.GetTempPath(), $"drill-cleanup-ro-{Guid.NewGuid():N}");
            Directory.CreateDirectory(readOnlyDir);
            target = Path.Combine(readOnlyDir, "undeletable.tmp");
            File.WriteAllText(target, "x");
            // chmod 0500 (r-x, no write) on the parent → unlink of the child is denied →
            // File.Delete throws UnauthorizedAccessException, which the cleanup's catch filter swallows.
            File.SetUnixFileMode(readOnlyDir, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }

        try
        {
            var act = () => DrillTempFileCleanup.TryDelete(new[] { target }, logger);
            act.Should().NotThrow();

            // The swallow path logs a Warning rather than propagating the I/O failure.
            logger.ReceivedWithAnyArgs().Log(
                LogLevel.Warning,
                Arg.Any<EventId>(),
                Arg.Any<object>(),
                Arg.Any<Exception>(),
                Arg.Any<Func<object, Exception?, string>>());
        }
        finally
        {
            lockHandle?.Dispose();
            // The OS check (not just the null check) is what lets the platform-compatibility analyzer
            // see that File.SetUnixFileMode is only reached off Windows.
            if (!OperatingSystem.IsWindows() && readOnlyDir is not null)
            {
                // Restore write so the throwaway dir can be cleaned up.
                File.SetUnixFileMode(readOnlyDir,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                Directory.Delete(readOnlyDir, recursive: true);
            }
            else if (File.Exists(target))
            {
                File.Delete(target);
            }
        }
    }
}
