using System.Text.RegularExpressions;
using FluentAssertions;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// Guards the queue-name coupling between producer and consumer: every queue constant declared in
/// <c>BackgroundJobQueues</c> MUST be listed in the Hangfire processing server's <c>opt.Queues</c>.
///
/// Failure mode this prevents: a queue constant is added and used at an enqueue call site, but the
/// server's queue list is never updated to include it. The job is then enqueued onto a queue no worker
/// drains and sits there forever (e.g. an uploaded file stuck Quarantined, an audit HMAC chain never
/// computed) — no exception, no failed job, just silent non-execution.
///
/// Pure source-text parse (no DI/EF probe), mirroring <see cref="HangfireRegistrationConsistencyTests"/>.
///
/// Scope limit: this guards CONSTANT-based queues only — it proves every <c>BackgroundJobQueues</c> constant
/// is registered, which is sound because callers are told to enqueue via those constants
/// (<c>IBackgroundJobService</c> docs). A raw-string enqueue (<c>BackgroundJob.Enqueue&lt;T&gt;("foo", …)</c>)
/// bypasses both the constant and this check; the convention, not this test, is what prevents that.
/// </summary>
public sealed class BackgroundJobQueueRegistrationTests
{
    private static readonly Regex QueueConstPattern = new(
        @"public\s+const\s+string\s+(\w+)\s*=",
        RegexOptions.Compiled);

    private static readonly Regex OptQueuesPattern = new(
        @"opt\.Queues\s*=\s*\[(?<list>[^\]]*)\]",
        RegexOptions.Compiled);

    [Fact]
    public void EveryBackgroundJobQueueConstant_Should_BeRegisteredOnTheProcessingServer()
    {
        var repoRoot = LocateRepoRoot();

        var queuesSource = File.ReadAllText(Path.Combine(repoRoot,
            "src", "shared", "AqtCoreFW.SharedKernel", "Application", "BackgroundJobQueues.cs"));
        var registrationSource = File.ReadAllText(Path.Combine(repoRoot,
            "src", "shared", "AqtCoreFW.Infrastructure", "BackgroundJobs", "HangfireRegistration.cs"));

        var declaredConstants = QueueConstPattern.Matches(queuesSource)
            .Select(m => m.Groups[1].Value)
            .ToList();
        declaredConstants.Should().NotBeEmpty(
            because: "BackgroundJobQueues should declare at least one queue constant");

        var optQueuesMatch = OptQueuesPattern.Match(registrationSource);
        optQueuesMatch.Success.Should().BeTrue(
            because: "HangfireRegistration must configure opt.Queues for the processing server");
        var optQueuesList = optQueuesMatch.Groups["list"].Value;

        var unregistered = declaredConstants
            .Where(name => !optQueuesList.Contains($"BackgroundJobQueues.{name}", StringComparison.Ordinal))
            .ToList();

        unregistered.Should().BeEmpty(
            because: "every BackgroundJobQueues constant must appear in the Hangfire server's opt.Queues " +
                     "(referenced as BackgroundJobQueues.<Name>) — a constant that is enqueued to but not " +
                     "listed leaves its jobs on a queue no worker drains, running nothing silently. " +
                     "Unregistered constants: " + string.Join(", ", unregistered));
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
