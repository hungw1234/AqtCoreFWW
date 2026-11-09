using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Identity.Application.Authorization;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Identity.Application;

/// <summary>Registers Identity Application layer: MediatR, validators, RBAC+ABAC policies.</summary>
public static class ModuleRegistration
{
    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // MediatR handlers + validators
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(ModuleRegistration).Assembly));
        services.AddValidatorsFromAssembly(typeof(ModuleRegistration).Assembly);

        // Permission-based policies: one per seeded permission code
        var authBuilder = services.AddAuthorizationBuilder();
        foreach (var code in PermCodes.All)
            authBuilder.AddPolicy(code, p => p.Requirements.Add(new PermissionRequirement(code)));

        // OR-semantics policy: doc-hash endpoint is reachable by a standalone form submitter
        // OR a business-process-run filler (different permission surfaces, same safe operation).
        authBuilder.AddPolicy(Policies.SignatureDocHashAccess, p =>
            p.AddRequirements(new AnyPermissionRequirement(
                PermCodes.Forms.Submit.Create, PermCodes.BusinessProcess.Bp.View)));

        // ABAC policies (attribute-based â€” distinct from permission-based)
        authBuilder
            .AddPolicy(Policies.DepartmentRestricted, p =>
                p.AddRequirements(new DepartmentAccessRequirement()))
            .AddPolicy(Policies.ClassifiedAccess, p =>
                p.AddRequirements(new ClassificationAccessRequirement()));

        // Authorization handlers
        services.AddScoped<IAuthorizationHandler, AnyPermissionAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, DepartmentAccessHandler>();
        services.AddScoped<IAuthorizationHandler, ClassificationAccessHandler>();

        return services;
    }
}
