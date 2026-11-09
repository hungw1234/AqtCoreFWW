using AqtCoreFW.Architecture.Tests.Fixtures;
using FluentAssertions;

namespace AqtCoreFW.Architecture.Tests;

public sealed class ContractsModuleArchitectureGuardTests
{
    [Fact]
    public void ContractsModule_IsRegisteredInLayerIsolationGuards()
    {
        var module = AssemblyFixtures.Modules
            .Should()
            .ContainSingle(candidate => candidate.Name == "Contracts")
            .Which;

        module.Domain.Should().BeSameAs(AssemblyFixtures.ContractsDomain);
        module.Application.Should().BeSameAs(AssemblyFixtures.ContractsApplication);
        module.Infrastructure.Should().BeSameAs(AssemblyFixtures.ContractsInfrastructure);
        module.Presentation.Should().BeSameAs(AssemblyFixtures.ContractsPresentation);
    }
}
