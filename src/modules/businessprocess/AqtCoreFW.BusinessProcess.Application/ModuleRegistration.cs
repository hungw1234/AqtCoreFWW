using AqtCoreFW.BusinessProcess.Application.ModuleClients;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.BusinessProcess.Application;

/// <summary>
/// Wires MediatR handler discovery + FluentValidation validators for the BusinessProcess module.
/// Mirrors the pattern used by other modules (Rules, Forms, Workflow).
/// </summary>
public static class ModuleRegistration
{
    public static IServiceCollection AddBusinessProcessModule(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(ModuleRegistration).Assembly));

        services.AddValidatorsFromAssembly(typeof(ModuleRegistration).Assembly);

        // BP MVP P6: prefill resolver. Scoped — pulls request-scoped tenant + db connections via deps.
        services.AddScoped<IFormPrefillContextClient, InProcessFormPrefillContextClient>();

        return services;
    }
}
