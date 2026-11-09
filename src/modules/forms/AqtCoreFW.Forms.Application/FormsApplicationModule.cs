using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Forms.Application;

public static class FormsApplicationModule
{
    public static IServiceCollection AddFormsModule(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(FormsApplicationModule).Assembly));

        services.AddValidatorsFromAssembly(typeof(FormsApplicationModule).Assembly);

        return services;
    }
}
