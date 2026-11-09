namespace AqtCoreFW.Notifications.Application.Services;

/// <summary>Renders Scriban templates with model data. Timeout 100ms enforced per spec.</summary>
public interface ITemplateRenderer
{
    /// <summary>Render template string with model object. Returns rendered string.</summary>
    Task<string> RenderAsync(string template, object model, CancellationToken cancellationToken = default);
}
