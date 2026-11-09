using System.Reflection;
using AqtCoreFW.Architecture.Tests.Fixtures;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// DI validation — catches the "handler class exists but AddMediatR scan missing" bug class.
///
/// Background: v2.131p4 Thin Controllers Refactor (commit 7e16e164, 2026-04-24) moved
/// SystemParams handlers into the Infrastructure assembly but forgot to add AddMediatR
/// scanning. All 9 MediatR-based endpoints returned 500 "No service for IRequestHandler"
/// at runtime. Symmetry-only arch tests could not catch it — handlers existed; only the
/// DI registration step was missing.
///
/// This test invokes each module's public static registration method(s) with a stub
/// IConfiguration, then asserts every concrete IRequestHandler class in the module's
/// Application + Infrastructure assemblies is present in the resulting ServiceCollection.
///
/// Expected failure mode (regression reproduction):
///   If SystemParamsInfrastructureRegistration.AddSystemParamsInfrastructure omits AddMediatR, the test fails with:
///     "SystemParams: missing MediatR registration for [GetAllAnnouncementsQueryHandler,
///     CreateAnnouncementCommandHandler, ...]. Did you forget AddMediatR(cfg =>
///     cfg.RegisterServicesFromAssembly(...))?"
/// </summary>
public sealed class ModuleMediatRRegistrationTests
{
    [Fact]
    public void AllModules_RegistrationMethods_RegisterAllMediatRHandlers()
    {
        var failures = new List<string>();

        foreach (var module in AssemblyFixtures.Modules)
        {
            var moduleAssemblies = new[] { module.Application, module.Infrastructure };

            var expectedHandlers = moduleAssemblies
                .SelectMany(SafeGetTypes)
                .Where(IsConcreteRequestHandler)
                .ToHashSet();

            if (expectedHandlers.Count == 0) continue;

            var services = new ServiceCollection();
            var stubConfig = BuildStubConfiguration();

            var registrationMethods = FindRegistrationMethods(moduleAssemblies).ToList();
            foreach (var method in registrationMethods)
            {
                TryInvokeRegistration(method, services, stubConfig);
            }

            var registeredImplTypes = services
                .Where(sd => sd.ServiceType.IsGenericType &&
                             sd.ServiceType.GetGenericTypeDefinition() == typeof(IRequestHandler<,>))
                .Select(sd => sd.ImplementationType)
                .Where(t => t is not null)
                .ToHashSet();

            var missing = expectedHandlers
                .Where(h => !registeredImplTypes.Contains(h))
                .Select(h => h.Name)
                .OrderBy(n => n)
                .ToList();

            if (missing.Count > 0)
            {
                failures.Add(
                    $"{module.Name}: missing MediatR registration for [{string.Join(", ", missing)}]. " +
                    "Did you forget AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(YourRegistration).Assembly)) " +
                    "in the module's Registration class?");
            }
        }

        failures.Should().BeEmpty(
            because: "each module's registration method(s) must call AddMediatR and scan the assembly/assemblies containing handlers — " +
                     "handlers silently not-registered cause 500 errors at runtime, not compile-time failures");
    }

    /// <summary>
    /// Convention guard — every module must expose at least one discoverable registration method
    /// (public static, IServiceCollection-first, on a class whose name contains "Registration" or ends
    /// with "Module"). The handler test above early-continues for modules with zero MediatR handlers,
    /// so a rename that breaks the class-name convention would un-guard such a module without any
    /// failure. Asserting presence directly across all modules makes a stray rename fail loud.
    /// </summary>
    [Fact]
    public void AllModules_ExposeDiscoverableRegistrationMethod()
    {
        var modulesWithoutRegistration = AssemblyFixtures.Modules
            .Where(m => !FindRegistrationMethods(new[] { m.Application, m.Infrastructure }).Any())
            .Select(m => m.Name)
            .OrderBy(n => n)
            .ToList();

        modulesWithoutRegistration.Should().BeEmpty(
            because: "each module must keep a public static Add*(IServiceCollection, ...) registration on a class " +
                     "whose name contains 'Registration' or ends with 'Module' — otherwise FindRegistrationMethods " +
                     "(and the MediatR-handler guard above) silently skips it, re-opening the false-green this convention closes");
    }

    /// <summary>Defensive against types referencing unloaded dependencies (happens with analyzer packages).</summary>
    private static IEnumerable<Type> SafeGetTypes(Assembly a)
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }

    private static bool IsConcreteRequestHandler(Type t) =>
        !t.IsAbstract && t.IsClass && !t.IsGenericTypeDefinition &&
        t.GetInterfaces().Any(i =>
            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>));

    /// <summary>Finds public static methods on *Registration/*Module classes that take IServiceCollection first.</summary>
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

    /// <summary>Minimal config covering the connection strings modules typically read at registration time.</summary>
    private static IConfiguration BuildStubConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Server=(local);Database=stub;Integrated Security=true;TrustServerCertificate=true",
                ["ConnectionStrings:Redis"] = "localhost:6379",
                ["Redis:ConnectionString"] = "localhost:6379",
                ["Hangfire:ConnectionString"] = "Server=(local);Database=stub;Integrated Security=true;TrustServerCertificate=true",
            })
            .Build();

    /// <summary>
    /// Invokes a registration method with best-effort arg matching. Failures are silent on purpose —
    /// some registrations touch external deps (IConnectionMultiplexer, IOptions&lt;T&gt;) not stubbable
    /// here. MediatR's own AddMediatR call is side-effect-free and completes even if later lines throw,
    /// so we still capture handler registrations from the crash-before point.
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
                else if (parameters[i].HasDefaultValue) args[i] = parameters[i].DefaultValue;
                else args[i] = pt.IsValueType ? Activator.CreateInstance(pt) : null;
            }

            method.Invoke(null, args);
        }
        catch
        {
            // Swallow — see XML doc. The only thing we assert is post-hoc ServiceCollection state.
        }
    }
}
