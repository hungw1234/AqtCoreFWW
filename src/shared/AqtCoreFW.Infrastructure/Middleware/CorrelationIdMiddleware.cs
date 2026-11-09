using System.Diagnostics;
using AqtCoreFW.SharedKernel.Abstractions;
using Serilog.Context;

namespace AqtCoreFW.Infrastructure.Middleware;

/// <summary>
/// Propagates X-Correlation-Id header across requests.
/// Creates new GUID if header absent. Enriches Serilog log context.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, IGuidGenerator guidGen)
{
    private const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault()
            ?? Activity.Current?.TraceId.ToString()
            ?? guidGen.NewIdMonotonic().ToString("N");

        context.Items[HeaderName] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}

public static class CorrelationIdMiddlewareExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) =>
        app.UseMiddleware<CorrelationIdMiddleware>();
}
