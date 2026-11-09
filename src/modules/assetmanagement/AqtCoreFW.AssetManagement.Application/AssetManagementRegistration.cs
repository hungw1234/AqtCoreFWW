using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.AssetManagement.Application;

/// <summary>DI registration for AssetManagement module Application layer.</summary>
public static class AssetManagementRegistration
{
    public static IServiceCollection AddAssetManagementModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMediatR(config =>
            config.RegisterServicesFromAssembly(typeof(AssetManagementRegistration).Assembly));
        services.AddValidatorsFromAssembly(typeof(AssetManagementRegistration).Assembly);
        return services;
    }
}
