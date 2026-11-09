using FluentResults;

namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// A doc-flow failure that carries the exact HTTP status the VGCA desktop tool expects. The doc-flow
/// endpoints do NOT use the app-wide <c>ApiResponse&lt;T&gt;</c> envelope — they return raw
/// <c>{ error }</c> bodies with vendor-specific status codes (401/404/400/500) — so the command result
/// must convey both the status and the message, and the thin controller maps them verbatim.
/// </summary>
public sealed class DocFlowError(int statusCode, string message) : Error(message)
{
    /// <summary>HTTP status code the controller returns for this failure.</summary>
    public int StatusCode { get; } = statusCode;
}
