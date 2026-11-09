using System.Net;
using System.Text.Json;
using AqtCoreFW.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Infrastructure.Middleware;

/// <summary>
/// Global exception handler — catches unhandled exceptions, returns RFC 9457 ProblemDetails.
/// Never leaks stack traces in production. Logs with correlation ID.
/// </summary>
public sealed class GlobalExceptionHandlerMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionHandlerMiddleware> logger,
    IHostEnvironment env,
    IGuidGenerator guidGen)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        var correlationId = context.Items["X-Correlation-Id"]?.ToString() ?? guidGen.NewIdMonotonic().ToString("N");

        logger.LogError(ex, "Unhandled exception. CorrelationId: {CorrelationId}", correlationId);

        // If the response has already started (e.g. a streaming endpoint like the CSV export flushed
        // headers/body before failing), we cannot rewrite the status code or append a ProblemDetails
        // body — doing so throws "response has already started" or glues JSON onto a partial payload.
        // Re-throw so the host aborts the connection; the client sees a truncated download, not a
        // corrupt one. Buffered endpoints (the common case) still get the RFC-9457 response below.
        if (context.Response.HasStarted)
        {
            logger.LogWarning(
                "Response already started; cannot write error body. Aborting. CorrelationId: {CorrelationId}",
                correlationId);
            throw ex;
        }

        var (statusCode, errorCode, message) = ex switch
        {
            DbUpdateConcurrencyException => (HttpStatusCode.Conflict, "GOV_SYS_003", "Data was modified by another user. Please refresh and try again."),
            ArgumentException => (HttpStatusCode.BadRequest, "GOV_SYS_002", "Invalid request parameter."),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "GOV_SEC_002", "Unauthorized."),
            _ => (HttpStatusCode.InternalServerError, "GOV_SYS_000", "An unexpected error occurred.")
        };

        var problem = new
        {
            type = $"https://gov.vn/errors/{errorCode.ToLowerInvariant()}",
            title = message,
            status = (int)statusCode,
            detail = env.IsDevelopment() ? ex.Message : "Contact support.",
            instance = context.Request.Path.Value,
            errorCode,
            correlationId,
            timestamp = DateTimeOffset.UtcNow
        };

        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOpts));
    }
}

public static class GlobalExceptionHandlerExtensions
{
    public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder app) =>
        app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
}
