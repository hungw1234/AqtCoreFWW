namespace AqtCoreFW.Files.Domain.Services;

/// <summary>
/// Renders a Scriban template string against a data model.
/// Implementation lives in Infrastructure (ScribanTemplateEngine).
/// Domain defines the contract — no Scriban dependency in Domain project.
/// </summary>
public interface ITemplateEngine
{
    /// <summary>
    /// Renders the given Scriban template using the provided model object.
    /// Returns the rendered string output.
    /// Throws TemplateRenderException on syntax or runtime errors.
    /// </summary>
    Task<string> RenderAsync(string templateContent, object model, CancellationToken cancellationToken = default);
}
