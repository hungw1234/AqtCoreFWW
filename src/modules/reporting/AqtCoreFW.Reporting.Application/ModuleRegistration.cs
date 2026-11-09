using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Reporting.Application;

public static class ModuleRegistration
{
    public static IServiceCollection AddReportingModule(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(ModuleRegistration).Assembly));

        services.AddValidatorsFromAssembly(typeof(ModuleRegistration).Assembly);

        return services;
    }
}
