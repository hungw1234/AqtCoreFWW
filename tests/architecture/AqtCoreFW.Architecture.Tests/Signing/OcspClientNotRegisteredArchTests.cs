using NSubstitute;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Hosting;
using System.Reflection;
using AqtCoreFW.Architecture.Tests.Fixtures;
using AqtCoreFW.Identity.Infrastructure.Services.Signing;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Architecture.Tests.Signing;

/// <summary>
/// Descope guard (RED-TEAM H1): verify-at-ingest signature validation runs OFFLINE PKIX for v1.0 —
/// no OCSP revocation round-trip. <see cref="IOcspClient"/> is deliberately left UNREGISTERED so
/// <c>CertificateChainValidator</c> receives <c>ocspClient = null</c> and reports
/// <c>OcspChecked = false</c> (see <c>CertificateChainValidatorTests.ValidChain_NoOcspClient_...</c>).
///
/// The unit test above only proves "null client → OcspChecked=false" for a hand-constructed validator.
/// It cannot catch the real regression: someone adding <c>services.AddScoped&lt;IOcspClient, ...&gt;()</c>
/// to a registration method would silently ARM OCSP with no outbound timeout/circuit-breaker (the
/// control the plan descoped precisely because the seam does not exist yet). Enabling OCSP is a
/// deliberate, reviewed change — wire it as a typed HttpClient with a timeout THERE, then delete this
/// guard. Until then this test drives the REAL registration path (not a hand-rolled container) so a
/// stray registration fails loud.
/// </summary>
public sealed class OcspClientNotRegisteredArchTests
{
    [Fact]
    public void IOcspClient_HasNoDiRegistration_AcrossEveryModuleRegistrationMethod()
    {
        var services = new ServiceCollection();
        var stubConfig = BuildStubConfiguration();

        // Invoke every discoverable module registration method against a shared collection.
        // Identity Infrastructure is where CertificateChainValidator + IOcspClient live, but scanning
        // all modules is the honest guard — an OCSP client wired from anywhere would still arm the path.
        foreach (var module in AssemblyFixtures.Modules)
        {
            foreach (var method in FindRegistrationMethods(new[] { module.Application, module.Infrastructure }))
                TryInvokeRegistration(method, services, stubConfig);
        }

        // Sentinel: the signing block of IdentityInfrastructureRegistration MUST have executed, otherwise
        // this guard passes vacuously. AddIdentityInfrastructure registers ICertificateChainValidator
        // immediately alongside where an IOcspClient registration would sit; if an early line throws (e.g. a
        // missing config key) TryInvokeRegistration swallows it and the signing registrations never land,
        // green-lighting the exact regression this test claims to block. Asserting the sibling IS present
        // turns that failure mode green→red.
        services.Should().Contain(sd => sd.ServiceType == typeof(ICertificateChainValidator),
            "the Identity signing registrations must have executed — otherwise the IOcspClient absence check " +
            "below is vacuous (an early throw in AddIdentityInfrastructure would skip the whole signing block)");

        var ocspRegistrations = services
            .Where(sd => sd.ServiceType == typeof(IOcspClient))
            .Select(sd => sd.ImplementationType?.Name ?? sd.ImplementationInstance?.GetType().Name ?? "factory")
            .ToList();

        ocspRegistrations.Should().BeEmpty(
            because: "v1.0 verify-at-ingest is offline PKIX (RED-TEAM H1): IOcspClient must stay unregistered so " +
                     "CertificateChainValidator runs with ocspClient=null and OcspChecked=false. Registering an OCSP " +
                     $"client arms revocation checks with no outbound timeout — wire it as a typed HttpClient with a " +
                     $"timeout + circuit-breaker and remove this guard when OCSP is intentionally enabled. Found: " +
                     $"[{string.Join(", ", ocspRegistrations)}]");
    }

    /// <summary>Finds public static Add*(IServiceCollection, ...) methods on *Registration/*Module static classes.</summary>
    private static IEnumerable<MethodInfo> FindRegistrationMethods(Assembly[] assemblies) =>
        assemblies
            .SelectMany(SafeGetTypes)
            .Where(t => t.IsAbstract && t.IsSealed) // C# static class
            .Where(t => t.Name.Contains("Registration") || t.Name.EndsWith("Module"))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.Name.StartsWith("Add"))
            .Where(m =>
            {
                var p = m.GetParameters();
                return p.Length >= 1 && typeof(IServiceCollection).IsAssignableFrom(p[0].ParameterType);
            });

    /// <summary>Defensive against types referencing unloaded analyzer dependencies.</summary>
    private static IEnumerable<Type> SafeGetTypes(Assembly a)
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }

    /// <summary>
    /// Best-effort registration invocation. Failures are swallowed on purpose — some registrations touch
    /// external deps (IConnectionMultiplexer, IOptions&lt;T&gt;) not stubbable here, but AddScoped calls are
    /// side-effect-free and land in the collection even when a later line throws. The only assertion is the
    /// post-hoc ServiceCollection state, so a partial invocation still surfaces an IOcspClient registration.
    /// </summary>
    private static void TryInvokeRegistration(MethodInfo method, IServiceCollection services, IConfiguration config)
    {
        try
        {
            var parameters = method.GetParameters();
            var args = new object?[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                var pt = parameters[i].ParameterType;
                if (typeof(IServiceCollection).IsAssignableFrom(pt)) args[i] = services;
                else if (typeof(IConfiguration).IsAssignableFrom(pt)) args[i] = config;
                // Registrations read the environment name (dev-only fallbacks); a null env would throw
                // before the signing block and make the sentinel above fail for the wrong reason.
                else if (typeof(IWebHostEnvironment).IsAssignableFrom(pt)) args[i] = DevelopmentEnvironment();
                else if (parameters[i].HasDefaultValue) args[i] = parameters[i].DefaultValue;
                else args[i] = pt.IsValueType ? Activator.CreateInstance(pt) : null;
            }

            method.Invoke(null, args);
        }
        catch
        {
            // Swallow — see XML doc. Only post-hoc ServiceCollection state is asserted.
        }
    }

    private static IWebHostEnvironment DevelopmentEnvironment()
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns(Environments.Development);
        return env;
    }

    /// <summary>
    /// Minimal config covering keys read during registration. <c>Auth:AuthorityUrl</c> is REQUIRED: without
    /// it <c>IdentityInfrastructureRegistration.AddIdentityInfrastructure</c> throws in the OpenIddict
    /// <c>AddValidation</c> block (before the signing registrations), which <c>TryInvokeRegistration</c>
    /// swallows — leaving the IOcspClient check vacuous. The <c>ICertificateChainValidator</c>-present
    /// sentinel above is the backstop that fails the test if this key is ever dropped again.
    /// </summary>
    private static IConfiguration BuildStubConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Server=(local);Database=stub;Integrated Security=true;TrustServerCertificate=true",
                ["ConnectionStrings:Redis"] = "localhost:6379",
                ["Redis:ConnectionString"] = "localhost:6379",
                ["Hangfire:ConnectionString"] = "Server=(local);Database=stub;Integrated Security=true;TrustServerCertificate=true",
                ["Auth:AuthorityUrl"] = "http://localhost:5002",
            })
            .Build();
}
