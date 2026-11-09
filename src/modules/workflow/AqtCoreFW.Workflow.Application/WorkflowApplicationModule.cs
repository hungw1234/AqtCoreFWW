using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Application.Validation;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Workflow.Application;

/// <summary>
/// Registers all Workflow Application layer services:
/// MediatR handlers, FluentValidation validators, and condition evaluator.
/// Called from host Program.cs: services.AddWorkflowModule()
/// </summary>
public static class WorkflowApplicationModule
{
    public static IServiceCollection AddWorkflowModule(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(WorkflowApplicationModule).Assembly));

        services.AddValidatorsFromAssembly(typeof(WorkflowApplicationModule).Assembly);

        services.AddScoped<IConditionEvaluator, DeclarativeConditionEvaluator>();
        services.AddScoped<IWorkflowAssignmentResolver, WorkflowAssignmentResolver>();
        services.AddScoped<IWorkflowHandlerResolver, WorkflowHandlerResolver>();
        services.AddScoped<IWorkflowTaskFactory, WorkflowTaskFactory>();
        services.AddScoped<IBranchJoinCompletionService, BranchJoinCompletionService>();

        // Phase 5c: workflow graph validator (R1-R4 design-time checks).
        // RT2-15 Phase 02: downgraded Singleton → Scoped — validator now injects IIdentityModuleClient
        // (scoped/DbContext-bound) for Q2 ByGroup Save-time code existence check. Stateless otherwise.
        services.AddScoped<IWorkflowGraphValidator, WorkflowGraphValidator>();

        return services;
    }
}
