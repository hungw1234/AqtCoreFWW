using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Ai.Application;

/// <summary>Registers MediatR handlers and validators for the AI module.</summary>
public static class AiApplicationModule
{
    public static IServiceCollection AddAiModule(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(AiApplicationModule).Assembly));

        services.AddValidatorsFromAssembly(typeof(AiApplicationModule).Assembly);

        return services;
    }
}
