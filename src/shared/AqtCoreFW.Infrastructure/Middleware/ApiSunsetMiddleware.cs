namespace AqtCoreFW.Infrastructure.Middleware;

/// <summary>
/// RFC 8594 Sunset header middleware — adds deprecation headers for retired API versions.
/// Config section: "Deprecation:DeprecatedRoutes" list of {Path, SunsetDate}.
/// Adds: Sunset (RFC 7231 date), Deprecation: true, Link rel="deprecation".
/// </summary>
public sealed class ApiSunsetMiddleware(
    RequestDelegate next,
    IConfiguration configuration)
{
    private readonly IReadOnlyList<DeprecatedRoute> _deprecatedRoutes = LoadRoutes(configuration);

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        var match = _deprecatedRoutes.FirstOrDefault(r =>
            path.StartsWith(r.PathPrefix, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            // RFC 7231 HTTP-date format
            var sunsetDate = match.SunsetDate.ToString("R");
            context.Response.Headers["Sunset"] = sunsetDate;
            context.Response.Headers["Deprecation"] = "true";
            context.Response.Headers["Link"] =
                "<https://docs.gov.vn/api/migration>; rel=\"deprecation\"";
        }

        await next(context);
    }

    private static List<DeprecatedRoute> LoadRoutes(IConfiguration config)
    {
        var routes = new List<DeprecatedRoute>();
        var section = config.GetSection("Deprecation:DeprecatedRoutes");
        foreach (var child in section.GetChildren())
        {
            var path = child["Path"];
            var dateStr = child["SunsetDate"];
            if (!string.IsNullOrEmpty(path)
                && DateTimeOffset.TryParse(dateStr, out var date))
            {
                routes.Add(new DeprecatedRoute(path.TrimEnd('*'), date));
            }
        }
        return routes;
    }

    private sealed record DeprecatedRoute(string PathPrefix, DateTimeOffset SunsetDate);
}

public static class ApiSunsetExtensions
{
    public static IApplicationBuilder UseApiSunset(this IApplicationBuilder app) =>
        app.UseMiddleware<ApiSunsetMiddleware>();
}
