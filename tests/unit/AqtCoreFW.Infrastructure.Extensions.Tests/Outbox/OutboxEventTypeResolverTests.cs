using AqtCoreFW.Infrastructure.Persistence.Outbox;
using AqtCoreFW.SharedKernel.Domain.Events;
using FluentAssertions;

namespace AqtCoreFW.Infrastructure.Extensions.Tests.Outbox;

/// <summary>
/// Unit tests for <see cref="OutboxEventTypeResolver"/>. Pure (no I/O, no DB/broker) —
/// exercises the FullName map (built from a real assembly scan of the loaded
/// "AqtCoreFW.*" assemblies, which includes this very test assembly) plus the legacy
/// AssemblyQualifiedName fallback.
/// </summary>
public sealed class OutboxEventTypeResolverTests
{
    /// <summary>Local IExternalDomainEvent implementer — proves the FullName happy path (a).</summary>
    private sealed record LocalExternalEvent(Guid MessageId) : IExternalDomainEvent
    {
        public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Plain class that does NOT implement IExternalDomainEvent — resolver's FullName map
    /// never contains it, so resolving it can only succeed via the legacy AQN fallback (b).
    /// </summary>
    private sealed class LegacyPayloadStandIn;

    [Fact]
    public void Resolve_KnownExternalDomainEventFullName_ReturnsType()
    {
        var resolver = new OutboxEventTypeResolver();

        var resolved = resolver.Resolve(typeof(LocalExternalEvent).FullName!);

        resolved.Should().Be(typeof(LocalExternalEvent));
    }

    [Fact]
    public void Resolve_LegacyAssemblyQualifiedName_FallsBackAndResolves()
    {
        var resolver = new OutboxEventTypeResolver();

        // Not an IExternalDomainEvent implementer → absent from the FullName map. Only the
        // AssemblyQualifiedName fallback (legacy rows persisted before the token cutover)
        // can resolve it.
        var resolved = resolver.Resolve(typeof(LegacyPayloadStandIn).AssemblyQualifiedName!);

        resolved.Should().Be(typeof(LegacyPayloadStandIn));
    }

    [Fact]
    public void Resolve_GarbageToken_ReturnsNull()
    {
        var resolver = new OutboxEventTypeResolver();

        var resolved = resolver.Resolve("Not.A.Real.Type, NoSuchAssembly, Version=1.0.0.0");

        resolved.Should().BeNull();
    }

    [Fact]
    public void Resolve_EmptyMapStillFallsBackToAssemblyQualifiedName()
    {
        // Guards against a regression where the fallback branch is accidentally skipped
        // when the FullName map lookup misses for any reason (not just "map is populated").
        var resolver = new OutboxEventTypeResolver();

        var resolved = resolver.Resolve(typeof(string).AssemblyQualifiedName!);

        resolved.Should().Be(typeof(string));
    }

    // NOTE — duplicate-FullName startup-throw guard (spec item "d") is intentionally NOT
    // covered by a live test here. Constructing two loaded assemblies that genuinely
    // declare the same FullName (the only way to prove the guard fires on real Type
    // identity, not just a same-Type-seen-twice no-op) requires either:
    //   1. Reflection.Emit / a second AssemblyLoadContext — both permanently register the
    //      colliding assemblies in this process (AppDomain.CurrentDomain.GetAssemblies()
    //      surfaces every ALC, not just the default one), which would make EVERY
    //      subsequent `new OutboxEventTypeResolver()` call in this test run (including
    //      OutboxDispatcherTests's) throw — a process-wide test-pollution regression, not
    //      a contained unit test; or
    //   2. exposing an internal test-only seam, which needs an InternalsVisibleTo entry in
    //      AqtCoreFW.Infrastructure.csproj — outside this slice's file ownership.
    // The guard clause itself (Dictionary key-collision → throw with both AssemblyQualifiedNames
    // in the message) is a 3-line, code-reviewable branch in OutboxEventTypeResolver.BuildMap.
}
