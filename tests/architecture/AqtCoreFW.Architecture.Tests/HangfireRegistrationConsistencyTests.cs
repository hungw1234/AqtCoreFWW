using System.Text.RegularExpressions;
using FluentAssertions;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// Guards against the 2026-05-02 incident class: a host registers
/// <c>HangfireAuditJobEnqueuer</c> (or its interface <c>IBackgroundAuditJobEnqueuer</c>)
/// without also calling <c>AddHangfire(...)</c> / <c>AddHangfireJobs(...)</c>.
///
/// Failure mode: <c>HangfireAuditJobEnqueuer.EnqueueHmacChain</c> calls the static
/// <c>BackgroundJob.Enqueue&lt;T&gt;</c>, which reads <c>JobStorage.Current</c>. That static
/// is only initialised by <c>AddHangfire</c>. A host that registers the enqueuer but never
/// configures Hangfire passes DI validation and crashes at runtime on the first audit log
/// with <c>InvalidOperationException: Current JobStorage instance has not been initialized</c>.
///
/// AuthServer hit this exact bug because audit infrastructure was registered manually
/// (without going through <c>AddAuditInfrastructure</c>) and the call to <c>AddHangfire</c>
/// was missed. The architecture suite did not catch it because no DI/EF probe runs here —
/// this test parses host source text directly.
///
/// Invariant: every <c>src/host/*/Program.cs</c> that mentions <c>IBackgroundAuditJobEnqueuer</c>
/// or <c>HangfireAuditJobEnqueuer</c> must also call <c>AddHangfire(</c> or
/// <c>AddHangfireJobs(</c> in the same file.
/// </summary>
public sealed class HangfireRegistrationConsistencyTests
{
    private static readonly Regex EnqueuerReferencePattern = new(
        @"(IBackgroundAuditJobEnqueuer|HangfireAuditJobEnqueuer)",
        RegexOptions.Compiled);

    private static readonly Regex HangfireRegistrationPattern = new(
        @"\.\s*AddHangfire\s*\(|\.\s*AddHangfireJobs\s*\(",
        RegexOptions.Compiled);

    [Fact]
    public void EveryHostRegisteringAuditJobEnqueuer_Should_AlsoRegisterHangfire()
    {
        var hostsRoot = Path.Combine(LocateRepoRoot(), "src", "host");
        Directory.Exists(hostsRoot).Should().BeTrue(because: $"expected hosts dir at {hostsRoot}");

        var offenders = new List<string>();

        foreach (var programFile in Directory.EnumerateFiles(hostsRoot, "Program.cs", SearchOption.AllDirectories))
        {
            // Skip bin/obj artefacts.
            if (programFile.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
            if (programFile.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;

            var text = File.ReadAllText(programFile);

            var registersEnqueuer = EnqueuerReferencePattern.IsMatch(text);
            if (!registersEnqueuer) continue;

            var registersHangfire = HangfireRegistrationPattern.IsMatch(text);
            if (!registersHangfire)
            {
                offenders.Add(Path.GetRelativePath(LocateRepoRoot(), programFile));
            }
        }

        offenders.Should().BeEmpty(
            because: "every host that wires IBackgroundAuditJobEnqueuer / HangfireAuditJobEnqueuer " +
                     "must also call AddHangfire(...) or AddHangfireJobs(...) — otherwise the static " +
                     "BackgroundJob.Enqueue inside the enqueuer crashes at runtime " +
                     "(InvalidOperationException: Current JobStorage instance has not been initialized). " +
                     "Offending hosts: " + string.Join(", ", offenders));
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            $"Could not locate repo root (parent of src/) from {AppContext.BaseDirectory} walking up.");
    }
}
