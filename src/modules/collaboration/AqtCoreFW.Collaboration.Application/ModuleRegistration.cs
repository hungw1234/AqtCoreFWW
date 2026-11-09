using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Collaboration.Application;

public static class ModuleRegistration
{
    public static IServiceCollection AddCollaborationModule(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(ModuleRegistration).Assembly));

        services.AddValidatorsFromAssembly(typeof(ModuleRegistration).Assembly);

        return services;
    }
}
