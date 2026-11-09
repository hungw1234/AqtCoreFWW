using AqtCoreFW.Files.Domain.Services;
using Microsoft.Extensions.Logging;
using Scriban;
using Scriban.Runtime;

namespace AqtCoreFW.Files.Infrastructure.Services;

/// <summary>
/// Scriban-based template rendering engine.
/// Implements ITemplateEngine from Domain — no Scriban dependency leaks into Domain.
/// Template is parsed once per call (no caching — callers cache if needed).
/// ScriptObject exposes model properties to template context via snake_case convention.
/// </summary>
public sealed class ScribanTemplateEngine(ILogger<ScribanTemplateEngine> logger)
    : ITemplateEngine
{
    public async Task<string> RenderAsync(
        string templateContent,
        object model,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var template = Template.Parse(templateContent);

            if (template.HasErrors)
            {
                var errors = string.Join("; ", template.Messages.Select(m => m.Message));
                throw new InvalidOperationException($"Template parse errors: {errors}");
            }

            var context = new TemplateContext { StrictVariables = false };
            var scriptObject = new ScriptObject();

            // Import model properties into script context
            // ScriptObject.Import reflects public properties into snake_case template variables
            scriptObject.Import(model, renamer: member => member.Name);
            context.PushGlobal(scriptObject);

            var result = await template.RenderAsync(context);
            return result;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            logger.LogError(ex, "Scriban template render failed.");
            throw new InvalidOperationException($"Template render failed: {ex.Message}", ex);
        }
    }
}
