using AqtCoreFW.Architecture.Tests.Fixtures;
using FluentAssertions;
using NetArchTest.Rules;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// Thin-Controller rule enforcement (added 2026-04-24 after audit found 2 controllers
/// — FilesController.List + UserSearchController.Search — bypassing MediatR).
///
/// Why this matters:
/// - Controllers that query DB directly skip MediatR pipeline (Validation, TenantAware, Logging behaviors)
/// - Cross-module enrichment (e.g. IIdentityModuleClient lookup) forces Presentation to reference
///   SharedKernel.Contracts — wrong layer
/// - Test surface lost — query logic only testable via slow integration tests
///
/// Rule: Controllers MUST be thin wrappers — only allowed deps are MediatR.ISender + Auth/Tenant
/// resolution. All DB access lives in Application handlers.
/// </summary>
public sealed class ThinControllerTests
{
    [Fact]
    public void AllModules_Controllers_Should_Not_Reference_Dapper()
    {
        foreach (var module in AssemblyFixtures.Modules)
        {
            var result = Types
                .InAssembly(module.Presentation)
                .That()
                .HaveNameEndingWith("Controller")
                .Should()
                .NotHaveDependencyOn("Dapper")
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                because: $"{module.Name} Controllers must not use Dapper directly — move query to Application.Queries/*Handler");
        }
    }

    [Fact]
    public void AllModules_Controllers_Should_Not_Reference_IReadDbConnection()
    {
        foreach (var module in AssemblyFixtures.Modules)
        {
            var result = Types
                .InAssembly(module.Presentation)
                .That()
                .HaveNameEndingWith("Controller")
                .Should()
                .NotHaveDependencyOn("AqtCoreFW.SharedKernel.Application.Data")
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                because: $"{module.Name} Controllers must not inject IReadDbConnection — DB access belongs in Application handlers");
        }
    }

    [Fact]
    public void AllModules_Controllers_Should_Not_Reference_EntityFrameworkCore()
    {
        foreach (var module in AssemblyFixtures.Modules)
        {
            var result = Types
                .InAssembly(module.Presentation)
                .That()
                .HaveNameEndingWith("Controller")
                .Should()
                .NotHaveDependencyOn("Microsoft.EntityFrameworkCore")
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                because: $"{module.Name} Controllers must not use DbContext directly — move query to Application handler");
        }
    }
}
