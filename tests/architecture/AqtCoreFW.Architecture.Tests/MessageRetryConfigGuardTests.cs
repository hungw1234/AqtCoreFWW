using FluentAssertions;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// Architecture guard: every MassTransit transport configured in
/// <c>MessagingRegistration.cs</c> MUST call <c>UseMessageRetry</c>.
///
/// Why this matters: several consumers are "throws-to-retry" — they signal a transient failure by
/// letting the exception propagate, relying on MassTransit's retry policy to redeliver (e.g.
/// <c>WorkflowFinalizedFormSubmissionConsumer</c>, whose retry-convergence is proven by
/// <c>TransientFaultRetryConvergenceTests</c>). Without <c>UseMessageRetry</c> on a transport, the
/// FIRST fault dead-letters the message immediately — no redelivery — silently converting every
/// transient failure into permanent message loss on that transport, with no behavioural test to
/// catch it (the InMemory bus is the only transport exercisable in tests; RabbitMQ is prod-only,
/// so a static source guard is the only way to cover BOTH transports).
///
/// This parses the committed registration source (like <see cref="MarsConnectionStringGuardTests"/>),
/// extracting each transport-configuration lambda body brace-depth-aware so a nested lambda
/// (e.g. <c>cfg.Host(host, vhost, h =&gt; { ... })</c>) does not confuse the block boundary.
///
/// Limitation (accepted for this guard): the block is matched as raw text, so a block that only
/// *mentions* the identifier in a comment would pass. The realistic regression — a refactor silently
/// dropping the <c>UseMessageRetry(...)</c> statement — leaves no such comment, so this is a
/// non-issue in practice; full comment/string-aware parsing would need Roslyn (over-engineering here).
/// </summary>
public sealed class MessageRetryConfigGuardTests
{
    private static readonly string RegistrationRelativePath = Path.Combine(
        "src", "shared", "AqtCoreFW.Infrastructure", "Messaging", "MessagingRegistration.cs");

    // The two transport-configuration entry points. Each occurs exactly once and each opens a
    // `(ctx, cfg) => { ... }` lambda whose body MUST configure UseMessageRetry.
    [Theory]
    [InlineData("UsingInMemory")]
    [InlineData("UsingRabbitMq")]
    public void EveryTransport_ConfiguresUseMessageRetry(string transportConfigurator)
    {
        var fullPath = Path.Combine(FindRepoRoot(), RegistrationRelativePath);
        File.Exists(fullPath).Should().BeTrue(
            $"expected the messaging registration at {RegistrationRelativePath}");

        var source = File.ReadAllText(fullPath);
        var block = ExtractLambdaBlock(source, transportConfigurator);

        block.Should().NotBeNull(
            $"the {transportConfigurator}(...) transport lambda must exist in {RegistrationRelativePath}");
        block!.Should().Contain("UseMessageRetry",
            because: $"the {transportConfigurator} transport must configure UseMessageRetry — without it a " +
                     "throws-to-retry consumer dead-letters on the first fault (no redelivery), silently " +
                     "turning every transient failure into permanent message loss on that transport.");
    }

    /// <summary>
    /// Returns the <c>{ ... }</c> body of the lambda passed to <c>&lt;marker&gt;(...)</c>, or null if the
    /// marker (or a balanced block after it) is not found. Brace-depth aware: nested blocks (a
    /// <c>Host(..., h =&gt; { ... })</c> lambda) are consumed within the outer body, not mistaken for its end.
    /// </summary>
    private static string? ExtractLambdaBlock(string source, string marker)
    {
        var markerIndex = source.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0) return null;

        var open = source.IndexOf('{', markerIndex);
        if (open < 0) return null;

        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0)
                return source[open..(i + 1)];
        }

        return null; // unbalanced braces — treat as not found so the guard fails loudly
    }

    /// <summary>
    /// Walks up from the test binary location until it finds a directory containing both
    /// <c>src/</c> and <c>tests/</c>. Mirrors the helper in <see cref="MarsConnectionStringGuardTests"/>.
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !(
            Directory.Exists(Path.Combine(dir.FullName, "src")) &&
            Directory.Exists(Path.Combine(dir.FullName, "tests"))))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull(
            $"could not locate repo root walking up from {AppContext.BaseDirectory} (expected src/ + tests/)");
        return dir!.FullName;
    }
}
