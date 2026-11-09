using AqtCoreFW.AssetManagement.Application;
using AqtCoreFW.AssetManagement.Application.Commands.CreateAssetManagement;
using AqtCoreFW.AssetManagement.Application.Queries.GetAssetManagement;
using AqtCoreFW.AssetManagement.Application.DTOs;
using AqtCoreFW.AssetManagement.Domain.Repositories;
using FluentAssertions;
using FluentResults;
using FluentValidation;
using AqtCoreFW.SharedKernel.Contracts;
using MediatR;
using AqtCoreFW.SharedKernel.Application.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace AqtCoreFW.AssetManagement.Application.Tests;

public sealed class AssetManagementRegistrationTests
{
    [Fact]
    public void AddAssetManagementModule_RegistersHandlersAndValidators()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IReadDbConnection>());
        services.AddSingleton(Substitute.For<IFeatureFlagService>());
        services.AddSingleton(Substitute.For<IAssetManagementRepository>());
        services.AddAssetManagementModule(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ISender>().Should().NotBeNull();
        provider.GetRequiredService<IRequestHandler<
            GetAssetManagementQuery,
            Result<AssetManagementDto>>>().Should().NotBeNull();
        provider.GetRequiredService<IRequestHandler<
            CreateAssetManagementCommand,
            Result<long>>>().Should().NotBeNull();
        provider.GetRequiredService<IValidator<CreateAssetManagementCommand>>()
            .Should().NotBeNull();
    }
}
