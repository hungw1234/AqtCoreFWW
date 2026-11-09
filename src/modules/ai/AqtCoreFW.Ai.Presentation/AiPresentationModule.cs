using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Ai.Presentation;

/// <summary>
/// Registration entry point for the AI Presentation layer.
/// Called from host startup: builder.Services.AddAiPresentation()
/// Controllers are auto-discovered by ASP.NET Core (DependencyContext) because this Presentation
/// assembly is ProjectReference'd by the host — no explicit AddApplicationPart is needed.
/// </summary>
public static class AiPresentationModule
{
    public static IServiceCollection AddAiPresentation(this IServiceCollection services)
    {
        // Controllers are auto-discovered via DependencyContext (project-referenced assembly) — no manual
        // registration needed. This method exists for any future presentation-layer services (filters, etc.).
        return services;
    }
}
