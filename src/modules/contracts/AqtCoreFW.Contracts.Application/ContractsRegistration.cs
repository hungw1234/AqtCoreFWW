using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Contracts.Application;

/// <summary>DI registration for Contracts module Application layer.</summary>
public static class ContractsRegistration
{
    public static IServiceCollection AddContractsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMediatR(config =>
            config.RegisterServicesFromAssembly(typeof(ContractsRegistration).Assembly));
        services.AddValidatorsFromAssembly(typeof(ContractsRegistration).Assembly);
        return services;
    }
}
