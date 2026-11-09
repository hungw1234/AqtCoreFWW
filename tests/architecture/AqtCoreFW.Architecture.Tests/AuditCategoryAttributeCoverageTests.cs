using System.Reflection;
using AqtCoreFW.Architecture.Tests.Fixtures;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using FluentAssertions;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// CI guard for QĐ 742 §3.1.b (Phase-01 Slice D1).
///
/// Every concrete <see cref="IBaseCommand"/> implementation in production module
/// Application assemblies MUST carry an <see cref="AuditCategoryAttribute"/> so the
/// MediatR <c>AuditBehavior</c> pipeline can persist the correct
/// <see cref="AuditCategory"/> on every audit log entry. The attribute is the only
/// authoritative classification source — verb-fallback in <c>AuditCategoryMapper</c>
/// is a soft default, not a substitute.
///
/// Failure mode: a new command added without the attribute would silently default to
/// <see cref="AuditCategory.AppAccess"/>, mis-classifying audit data and breaking the
/// QĐ 742 evidence chain. Strict-fail at architecture-test time prevents the drift.
/// </summary>
public sealed class AuditCategoryAttributeCoverageTests
{
    [Fact]
    public void AllConcreteIBaseCommandTypes_HaveAuditCategoryAttribute()
    {
        var missing = AssemblyFixtures.Modules
            .SelectMany(m => SafeGetTypes(m.Application).Select(t => (Module: m.Name, Type: t)))
            .Where(x => IsConcreteBaseCommand(x.Type))
            .Where(x => x.Type.GetCustomAttribute<AuditCategoryAttribute>(inherit: true) is null)
            .Select(x => $"{x.Module}: {x.Type.FullName}")
            .OrderBy(s => s)
            .ToList();

        missing.Should().BeEmpty(
            because: "every IBaseCommand must declare its QĐ 742 §3.1.b category via [AuditCategory(...)]; " +
                     "missing attribute → command silently maps to AuditCategory.AppAccess via verb fallback, " +
                     "corrupting audit-log classification. " +
                     "Decorate the command class/record with [AuditCategory(AuditCategory.X)] from " +
                     "AqtCoreFW.SharedKernel.Compliance.");
    }

    private static bool IsConcreteBaseCommand(Type t) =>
        !t.IsAbstract && !t.IsInterface && !t.IsGenericTypeDefinition &&
        typeof(IBaseCommand).IsAssignableFrom(t);

    /// <summary>Defensive against types referencing unloaded dependencies (mirror of ModuleMediatRRegistrationTests).</summary>
    private static IEnumerable<Type> SafeGetTypes(Assembly a)
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }
}
