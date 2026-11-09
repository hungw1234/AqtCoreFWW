using System.Reflection;
using System.Text.RegularExpressions;
using AqtCoreFW.Architecture.Tests.Fixtures;
using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// Phase-04d B10 guard: every entity carrying an external AK identifier must have the unique
/// index that protects against IDOR and duplicate-AK insertion.
///
/// Two coverage rules:
/// <list type="bullet">
///   <item><b>IPublicIdentified entities inheriting Entity</b> → must have a configuration class
///         deriving from <see cref="EntityTypeConfigurationBase{T}"/> or
///         <see cref="HotTableConfigurationBase{T}"/>. The base class auto-applies
///         <c>HasIndex(PublicId).IsUnique()</c> via <c>ConfigurePublicAndMessageIds</c>.</item>
///   <item><b>IMessageIdentified entities</b> → either inherit base (same auto-coverage) OR for
///         T3 plain-class entities (no <c>: Entity</c>) declare manual <c>HasAlternateKey(MessageId)</c>
///         or <c>HasIndex(...MessageId).IsUnique()</c> in their <c>IEntityTypeConfiguration&lt;T&gt;</c>.</item>
/// </list>
///
/// Without these indexes, T2/T3 records can be inserted with duplicate PublicId/MessageId values
/// and queries cannot use the AK as a tenant-safe lookup key.
/// </summary>
public sealed class PublicMessageIdAkCoverageTests
{
    private static readonly Regex MessageIdAkPattern = new(
        @"HasAlternateKey\s*\([^)]*MessageId|HasIndex\s*\([^)]*MessageId[^)]*\)\s*\.IsUnique",
        RegexOptions.Compiled);

    private static IEnumerable<Assembly> DomainAssemblies =>
        AssemblyFixtures.Modules.Select(m => m.Domain).Distinct();

    private static IEnumerable<Assembly> InfrastructureAssemblies =>
        AssemblyFixtures.Modules.Select(m => m.Infrastructure).Distinct();

    [Fact]
    public void EveryIPublicIdentifiedEntity_Should_UseEntityTypeConfigurationBase()
    {
        var entityTypes = DomainAssemblies
            .SelectMany(a => SafeGetTypes(a))
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && typeof(IPublicIdentified).IsAssignableFrom(t)
                        && typeof(Entity).IsAssignableFrom(t))
            .ToList();

        var configTypes = InfrastructureAssemblies
            .SelectMany(a => SafeGetTypes(a))
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .ToList();

        var violations = new List<string>();
        foreach (var entity in entityTypes)
        {
            var config = configTypes.FirstOrDefault(c => ImplementsEntityTypeConfiguration(c, entity));
            if (config is null)
            {
                violations.Add($"{entity.FullName}: no IEntityTypeConfiguration<> found");
                continue;
            }

            if (!InheritsConfigurationBase(config))
            {
                violations.Add(
                    $"{entity.FullName}: configuration {config.Name} does not derive from " +
                    $"EntityTypeConfigurationBase<{entity.Name}> or HotTableConfigurationBase<{entity.Name}> " +
                    "— PublicId AK unique index would be missing.");
            }
        }

        violations.Should().BeEmpty(
            because: "every IPublicIdentified entity that inherits Entity must use EntityTypeConfigurationBase " +
                     "(or HotTableConfigurationBase) so that ConfigurePublicAndMessageIds applies the unique AK index.");
    }

    [Fact]
    public void EveryIMessageIdentifiedEntity_Should_HaveMessageIdAkIndex()
    {
        var entityTypes = DomainAssemblies
            .SelectMany(a => SafeGetTypes(a))
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && typeof(IMessageIdentified).IsAssignableFrom(t))
            .ToList();

        var configTypes = InfrastructureAssemblies
            .SelectMany(a => SafeGetTypes(a))
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .ToList();

        var violations = new List<string>();
        foreach (var entity in entityTypes)
        {
            var config = configTypes.FirstOrDefault(c => ImplementsEntityTypeConfiguration(c, entity));
            if (config is null)
            {
                violations.Add($"{entity.FullName}: no IEntityTypeConfiguration<> found");
                continue;
            }

            // Path 1: config derives from base — automatic AK index applies.
            if (InheritsConfigurationBase(config)) continue;

            // Path 2: T3 plain-class — must declare manual HasAlternateKey(MessageId) or
            // HasIndex(MessageId).IsUnique() in the source file.
            var sourcePath = LocateConfigSource(config);
            if (sourcePath is null)
            {
                violations.Add($"{entity.FullName}: configuration {config.Name} source not located");
                continue;
            }

            var sourceText = File.ReadAllText(sourcePath);
            if (!MessageIdAkPattern.IsMatch(sourceText))
            {
                violations.Add(
                    $"{entity.FullName}: configuration {config.Name} ({Path.GetFileName(sourcePath)}) " +
                    "lacks both base-class inheritance and manual HasAlternateKey(MessageId)/HasIndex(MessageId).IsUnique() — " +
                    "MessageId AK unique guarantee is missing.");
            }
        }

        violations.Should().BeEmpty(
            because: "every IMessageIdentified entity must have a unique AK index on MessageId — either " +
                     "auto-applied via EntityTypeConfigurationBase or manually declared for T3 plain-class entities.");
    }

    private static bool ImplementsEntityTypeConfiguration(Type config, Type entity) =>
        config.GetInterfaces().Any(i =>
            i.IsGenericType
            && i.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>)
            && i.GetGenericArguments()[0] == entity);

    private static bool InheritsConfigurationBase(Type config)
    {
        var t = config.BaseType;
        while (t is not null && t != typeof(object))
        {
            if (t.IsGenericType)
            {
                var def = t.GetGenericTypeDefinition();
                if (def == typeof(EntityTypeConfigurationBase<>) || def == typeof(HotTableConfigurationBase<>))
                    return true;
            }
            t = t.BaseType;
        }
        return false;
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly a)
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }

    private static string? LocateConfigSource(Type config)
    {
        var repoRoot = LocateRepoRoot();
        var fileName = config.Name + ".cs";
        return Directory.EnumerateFiles(Path.Combine(repoRoot, "src", "modules"), fileName, SearchOption.AllDirectories)
            .FirstOrDefault();
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
            $"Could not locate repo root from {AppContext.BaseDirectory}.");
    }
}
