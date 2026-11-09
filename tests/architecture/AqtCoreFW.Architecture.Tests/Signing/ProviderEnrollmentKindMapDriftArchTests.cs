using System.Reflection;
using System.Runtime.CompilerServices;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Services.Signing;
using FluentAssertions;

namespace AqtCoreFW.Architecture.Tests.Signing;

/// <summary>
/// Drift guard for <see cref="ProviderEnrollmentKindMap"/>: every concrete
/// <see cref="IDigitalSignatureProvider"/> in Identity.Infrastructure MUST have a map entry. A provider
/// registered without a mapping would be denied at runtime (fail-closed, reason=<c>unmapped</c>) — this test
/// turns that latent 403 into a CI failure so the mapping is added in the SAME change as the provider (the
/// runtime deny remains the belt-and-braces backstop). Test fakes live in test assemblies and are excluded by
/// scanning only the Infrastructure assembly.
/// </summary>
public sealed class ProviderEnrollmentKindMapDriftArchTests
{
    private static readonly Assembly IdentityInfrastructureAssembly =
        typeof(UsbTokenSignatureProvider).Assembly;

    [Fact]
    public void EveryConcreteProvider_HasEnrollmentKindMapEntry()
    {
        var providerType = typeof(IDigitalSignatureProvider);
        var providers = IdentityInfrastructureAssembly
            .GetTypes()
            .Where(t => providerType.IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .ToList();

        providers.Should().NotBeEmpty("at least one IDigitalSignatureProvider implementation is expected");

        var map = new ProviderEnrollmentKindMap();
        foreach (var impl in providers)
        {
            // ProviderKey is an instance getter returning a literal (no field access) — an uninitialized
            // instance reads it safely without invoking the (dependency-heavy) constructor.
            var instance = RuntimeHelpers.GetUninitializedObject(impl);
            var providerKey = (string)impl
                .GetProperty(nameof(IDigitalSignatureProvider.ProviderKey))!
                .GetValue(instance)!;

            map.TryResolve(providerKey, out _).Should().BeTrue(
                $"provider '{impl.Name}' (key '{providerKey}') is registered but absent from " +
                $"{nameof(ProviderEnrollmentKindMap)} — add the map entry so the allowlist guard can authorize it " +
                "(a missing entry is a fail-closed 403 at runtime).");
        }
    }
}
