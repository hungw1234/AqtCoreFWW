using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Rules.Application;

public static class ModuleRegistration
{
    public static IServiceCollection AddRulesModule(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(ModuleRegistration).Assembly));

        services.AddValidatorsFromAssembly(typeof(ModuleRegistration).Assembly);

        return services;
    }
}
