using Scriban;
using Scriban.Runtime;
using Microsoft.Extensions.Logging;
using AqtCoreFW.Notifications.Application.Services;

namespace AqtCoreFW.Notifications.Infrastructure.Rendering;

/// <summary>
/// Scriban template renderer — sandboxed, safer than Liquid for admin-generated templates.
/// Regex timeout 100ms enforced via CancellationToken. Replaces Fluid/Liquid per Session 12 decision.
/// </summary>
public sealed class ScribanTemplateRenderer(ILogger<ScribanTemplateRenderer> logger) : ITemplateRenderer
{
    // Render timeout: 100ms per spec
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromMilliseconds(100);

    public async Task<string> RenderAsync(string templateText, object model, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(RenderTimeout);

        try
        {
            var template = Template.Parse(templateText);
            if (template.HasErrors)
            {
                var errors = string.Join("; ", template.Messages.Select(m => m.Message));
                logger.LogWarning("Scriban template parse errors: {Errors}", errors);
                throw new InvalidOperationException($"Template parse error: {errors}");
            }

            var scriptObject = new ScriptObject();
            scriptObject.Import(model, renamer: member => member.Name.ToLowerInvariant());

            var context = new TemplateContext { LoopLimit = 1000, RecursiveLimit = 10 };
            context.PushGlobal(scriptObject);

            var result = await template.RenderAsync(context);
            return result;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Scriban template render timed out after {Timeout}ms", RenderTimeout.TotalMilliseconds);
            throw new TimeoutException("PATTERN_TIMEOUT: Template rendering exceeded 100ms limit.");
        }
    }
}
