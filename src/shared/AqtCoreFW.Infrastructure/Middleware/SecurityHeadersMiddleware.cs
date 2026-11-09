namespace AqtCoreFW.Infrastructure.Middleware;

/// <summary>
/// OWASP security headers — must be registered BEFORE routing.
/// CSP is strict (default-src 'none') for API; relaxed for /swagger + /scalar.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    private static readonly string[] SwaggerPaths = ["/swagger", "/scalar"];

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        headers["X-XSS-Protection"] = "1; mode=block";

        // Unconditional HSTS — safe behind TLS-terminating reverse proxy (nginx/YARP)
        headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";

        // CSP: strict for API, relaxed for docs
        var isDocPath = SwaggerPaths.Any(p => context.Request.Path.StartsWithSegments(p));
        headers["Content-Security-Policy"] = isDocPath
            ? "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; frame-ancestors 'none'"
            : "default-src 'none'; frame-ancestors 'none'";

        await next(context);
    }
}

public static class SecurityHeadersExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.UseMiddleware<SecurityHeadersMiddleware>();
}
