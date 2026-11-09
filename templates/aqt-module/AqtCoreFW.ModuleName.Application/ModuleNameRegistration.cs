using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.ModuleName.Application;

/// <summary>DI registration for ModuleName module Application layer.</summary>
public static class ModuleNameRegistration
{
    public static IServiceCollection AddModuleNameModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMediatR(config =>
            config.RegisterServicesFromAssembly(typeof(ModuleNameRegistration).Assembly));
        services.AddValidatorsFromAssembly(typeof(ModuleNameRegistration).Assembly);
        return services;
    }
}
