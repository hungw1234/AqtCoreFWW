using AqtCoreFW.Architecture.Tests.Fixtures;
using AqtCoreFW.Architecture.Tests.Helpers;
using FluentAssertions;
using NetArchTest.Rules;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// Architecture enforcement tests using NetArchTest.Rules.
/// Verifies Clean/Onion layer dependency rules and cross-module isolation.
/// Failure here means an architectural boundary was broken — fix the source, not the test.
///
/// Coverage is data-driven via AssemblyFixtures.Modules — adding a module there
/// automatically enrolls it in all layer + cross-module + convention checks.
/// </summary>
public sealed class ModuleIsolationTests
{
    // =========================================================
    // Layer isolation — Domain/Application must not reach up
    // =========================================================

    [Fact]
    public void AllModules_DomainLayer_Should_Not_Reference_Infrastructure()
    {
        foreach (var module in AssemblyFixtures.Modules)
        {
            var result = Types
                .InAssembly(module.Domain)
                .Should()
                .NotHaveDependencyOn($"AqtCoreFW.{module.Name}.Infrastructure")
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                because: $"{module.Name} Domain must not depend on Infrastructure (Clean Architecture)");
        }
    }

    [Fact]
    public void AllModules_DomainLayer_Should_Not_Reference_Presentation()
    {
        foreach (var module in AssemblyFixtures.Modules)
        {
            var result = Types
                .InAssembly(module.Domain)
                .Should()
                .NotHaveDependencyOn($"AqtCoreFW.{module.Name}.Presentation")
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                because: $"{module.Name} Domain must not depend on Presentation");
        }
    }

    [Fact]
    public void AllModules_ApplicationLayer_Should_Not_Reference_Infrastructure()
    {
        foreach (var module in AssemblyFixtures.Modules)
        {
            var result = Types
                .InAssembly(module.Application)
                .Should()
                .NotHaveDependencyOn($"AqtCoreFW.{module.Name}.Infrastructure")
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                because: $"{module.Name} Application must not depend on Infrastructure — use interfaces/ports");
        }
    }

    [Fact]
    public void AllModules_ApplicationLayer_Should_Not_Reference_Presentation()
    {
        foreach (var module in AssemblyFixtures.Modules)
        {
            var result = Types
                .InAssembly(module.Application)
                .Should()
                .NotHaveDependencyOn($"AqtCoreFW.{module.Name}.Presentation")
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                because: $"{module.Name} Application must not depend on Presentation");
        }
    }

    // =========================================================
    // Cross-module isolation — modules must not directly reference each other
    // (SharedKernel interfaces like ICurrentUser are the allowed contract)
    // =========================================================

    [Fact]
    public void NoModule_Should_DirectlyReference_AnotherModule()
    {
        foreach (var source in AssemblyFixtures.Modules)
        {
            // Build list of all OTHER module namespaces (Domain/Application/Infrastructure)
            var otherModuleNamespaces = AssemblyFixtures.Modules
                .Where(m => m.Name != source.Name)
                .SelectMany(m => new[]
                {
                    $"AqtCoreFW.{m.Name}.Domain",
                    $"AqtCoreFW.{m.Name}.Application",
                    $"AqtCoreFW.{m.Name}.Infrastructure"
                })
                .ToArray();

            // Check all 4 layers — modules must not couple to each other's internals
            var sourceAssemblies = new[] { source.Domain, source.Application, source.Infrastructure, source.Presentation };

            foreach (var assembly in sourceAssemblies)
            {
                var result = Types
                    .InAssembly(assembly)
                    .Should()
                    .NotHaveDependencyOnAny(otherModuleNamespaces)
                    .GetResult();

                result.IsSuccessful.Should().BeTrue(
                    because: $"{assembly.GetName().Name} must not directly reference other modules — use SharedKernel contracts");
            }
        }
    }

    // =========================================================
    // Controller conventions — all controllers extend ApiControllerBase
    // =========================================================

    [Fact]
    public void AllModules_Controllers_Should_InheritApiControllerBase()
    {
        foreach (var module in AssemblyFixtures.Modules)
        {
            var result = Types
                .InAssembly(module.Presentation)
                .That()
                .HaveNameEndingWith("Controller")
                .Should()
                .Inherit(typeof(AqtCoreFW.SharedKernel.Presentation.ApiControllerBase))
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                because: $"All {module.Name} controllers must extend ApiControllerBase for consistent ApiResponse<T> envelope");
        }
    }

    // =========================================================
    // Naming conventions
    // =========================================================

    [Fact]
    public void AllModules_CommandHandlers_Should_HaveHandlerSuffix()
    {
        foreach (var module in AssemblyFixtures.Modules)
        {
            var result = Types
                .InAssembly(module.Application)
                .That()
                .ImplementInterface(typeof(MediatR.IRequestHandler<,>))
                .Should()
                .HaveNameEndingWith("Handler")
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                because: $"{module.Name} Command/Query handlers must be named *Handler by convention");
        }
    }

    [Fact]
    public void AllModules_DomainEntities_Should_ResideInEntitiesNamespace()
    {
        foreach (var module in AssemblyFixtures.Modules)
        {
            var result = Types
                .InAssembly(module.Domain)
                .That()
                .Inherit(typeof(AqtCoreFW.SharedKernel.Domain.AuditableEntity))
                .Should()
                .ResideInNamespaceContaining("Entities")
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                because: $"{module.Name} domain entities should live in the Entities namespace");
        }
    }

    [Fact]
    public void AllModules_DomainExceptions_Should_InheritFromSystemException()
    {
        foreach (var module in AssemblyFixtures.Modules)
        {
            var result = Types
                .InAssembly(module.Domain)
                .That()
                .HaveNameEndingWith("Exception")
                .Should()
                .Inherit(typeof(Exception))
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                because: $"{module.Name} exceptions must inherit from System.Exception");
        }
    }

    // =========================================================
    // SharedKernel must remain infrastructure-free
    // =========================================================

    [Fact]
    public void SharedKernel_Should_Not_Reference_EntityFrameworkCore()
    {
        var result = Types
            .InAssembly(AssemblyFixtures.SharedKernel)
            .Should()
            .NotHaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "SharedKernel is infrastructure-agnostic — no EF Core references allowed");
    }

    [Fact]
    public void SharedKernel_Should_Not_Reference_Dapper()
    {
        var result = Types
            .InAssembly(AssemblyFixtures.SharedKernel)
            .Should()
            .NotHaveDependencyOn("Dapper")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    // =========================================================
    // Cross-schema SQL coupling — permanent guard for Phase 01 fix
    // =========================================================

    /// <summary>
    /// Detects cross-schema SQL JOINs (e.g. <c>FROM [identity].Users</c> embedded in
    /// the Reporting module). Such coupling blocks per-module microservice extraction —
    /// modules MUST go through <c>IXxxModuleClient</c> interfaces or integration events
    /// for cross-module data, not raw SQL. Whitelist + module→schema map live in
    /// <see cref="CrossSchemaSqlScanner"/> — every whitelist entry carries a Reason.
    ///
    /// ACTIVATED v2.132p10 — 3 of 4 known cross-schema sites refactored through ModuleClient
    /// interfaces (Cases.Contracts, Audit.Contracts, OrgUnit.Contracts.ExpandSubtreeAsync).
    /// 4th site (OllamaReActAgent in deferred Ai module) whitelisted with Reason — see
    /// backlog CSQ-5. Whitelist tightened to per-(file,schema) entries. See plan
    /// 260428-1109-microservice-extraction-prep-closure.
    /// </summary>
    [Fact]
    public void NoModule_Should_Reference_AnotherModuleSchema_InRawSql()
    {
        var modulesRoot = LocateModulesRoot();

        var violations = CrossSchemaSqlScanner.Scan(modulesRoot);

        violations.Should().BeEmpty(
            because: "Cross-schema SQL JOINs couple modules at the database layer and prevent " +
                     "microservice extraction. Use IXxxModuleClient interfaces or expose the data via " +
                     "an integration event. If the reference is legitimate (e.g. seed-only), add a " +
                     "whitelist entry with Reason in CrossSchemaSqlScanner.Whitelist. Violations:\n" +
                     string.Join('\n', violations.Select(v =>
                         $"  {v.OwningModule} → [{v.ReferencedSchema}] in {v.FilePath}:{v.LineNumber}: {v.Snippet}")));
    }

    /// <summary>
    /// Walk up from the test assembly location until we find <c>src/modules</c>.
    /// Robust to bin/Debug/net10.0 nesting depth across machines + CI.
    /// </summary>
    private static string LocateModulesRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "modules");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            $"Could not locate src/modules from {AppContext.BaseDirectory} walking up.");
    }
}
