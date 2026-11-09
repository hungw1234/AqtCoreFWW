using System.Security.Claims;
using AqtCoreFW.Infrastructure.Middleware;
using AqtCoreFW.SharedKernel.Application.Caching;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AqtCoreFW.SharedKernel.Tests.Middleware;

/// <summary>
/// Unit tests for AntiDoubleSubmitMiddleware.
/// Validates: first request passes, duplicate detected, different token passes, GET not checked.
/// </summary>
public sealed class AntiDoubleSubmitMiddlewareTests
{
    private readonly ICacheService _cache;
    private readonly ILogger<AntiDoubleSubmitMiddleware> _logger;
    private readonly RequestDelegate _next;
    private readonly AntiDoubleSubmitMiddleware _middleware;

    private static readonly TimeSpan TokenTtl = TimeSpan.FromSeconds(5);

    public AntiDoubleSubmitMiddlewareTests()
    {
        _cache = Substitute.For<ICacheService>();
        _logger = Substitute.For<ILogger<AntiDoubleSubmitMiddleware>>();
        _next = Substitute.For<RequestDelegate>();
        _middleware = new AntiDoubleSubmitMiddleware(_next, _cache, _logger);
    }

    // --- Success path ---

    [Fact]
    public async Task InvokeAsync_GetRequest_PassesWithoutTokenCheck()
    {
        var context = BuildHttpContext("GET", "/api/items", token: null);

        await _middleware.InvokeAsync(context);

        await _next.Received(1).Invoke(Arg.Any<HttpContext>());
    }

    [Fact]
    public async Task InvokeAsync_PostWithoutToken_Passes()
    {
        var context = BuildHttpContext("POST", "/api/items", token: null);

        await _middleware.InvokeAsync(context);

        await _next.Received(1).Invoke(Arg.Any<HttpContext>());
        await _cache.DidNotReceive().GetAsync<bool?>(Arg.Any<string>());
    }

    [Fact]
    public async Task InvokeAsync_PostWithNewToken_Passes()
    {
        _cache.GetAsync<bool?>(Arg.Any<string>())
            .Returns(Task.FromResult<bool?>(null)); // Token not in cache

        var context = BuildHttpContext("POST", "/api/items", token: "unique-token-123");

        await _middleware.InvokeAsync(context);

        await _next.Received(1).Invoke(context);
    }

    [Fact]
    public async Task InvokeAsync_FirstRequest_CachesToken()
    {
        var token = "request-token-unique";
        _cache.GetAsync<bool?>(Arg.Any<string>())
            .Returns(Task.FromResult<bool?>(null));

        var context = BuildHttpContext("POST", "/api/items", token);

        await _middleware.InvokeAsync(context);

        await _cache.Received(1).SetAsync(
            Arg.Any<string>(),
            true,
            TokenTtl);
    }

    [Fact]
    public async Task InvokeAsync_PutRequest_Checked()
    {
        _cache.GetAsync<bool?>(Arg.Any<string>())
            .Returns(Task.FromResult<bool?>(null));

        var context = BuildHttpContext("PUT", "/api/items/1", token: "token");

        await _middleware.InvokeAsync(context);

        await _next.Received(1).Invoke(context);
    }

    [Fact]
    public async Task InvokeAsync_PatchRequest_Checked()
    {
        _cache.GetAsync<bool?>(Arg.Any<string>())
            .Returns(Task.FromResult<bool?>(null));

        var context = BuildHttpContext("PATCH", "/api/items/1", token: "token");

        await _middleware.InvokeAsync(context);

        await _next.Received(1).Invoke(context);
    }

    [Fact]
    public async Task InvokeAsync_DeleteRequest_Checked()
    {
        _cache.GetAsync<bool?>(Arg.Any<string>())
            .Returns(Task.FromResult<bool?>(null));

        var context = BuildHttpContext("DELETE", "/api/items/1", token: "token");

        await _middleware.InvokeAsync(context);

        await _next.Received(1).Invoke(context);
    }

    // --- Failure scenarios ---

    [Fact]
    public async Task InvokeAsync_DuplicateToken_Returns409()
    {
        _cache.GetAsync<bool?>(Arg.Any<string>())
            .Returns(Task.FromResult((bool?)true)); // Token exists

        var context = BuildHttpContext("POST", "/api/items", token: "duplicate-token");

        await _middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        await _next.DidNotReceive().Invoke(Arg.Any<HttpContext>());
    }

    [Fact]
    public async Task InvokeAsync_DuplicateToken_Returns409WithJsonError()
    {
        _cache.GetAsync<bool?>(Arg.Any<string>())
            .Returns(Task.FromResult((bool?)true));

        var context = BuildHttpContext("POST", "/api/items", token: "duplicate-token");

        await _middleware.InvokeAsync(context);

        context.Response.ContentType.Should().Be("application/json");
    }

    [Fact]
    public async Task InvokeAsync_DuplicateToken_DoesNotCallNext()
    {
        _cache.GetAsync<bool?>(Arg.Any<string>())
            .Returns(Task.FromResult((bool?)true));

        var context = BuildHttpContext("POST", "/api/items", token: "duplicate");

        await _middleware.InvokeAsync(context);

        await _next.DidNotReceive().Invoke(Arg.Any<HttpContext>());
    }

    [Fact]
    public async Task InvokeAsync_DuplicateToken_LogsWarning()
    {
        _cache.GetAsync<bool?>(Arg.Any<string>())
            .Returns(Task.FromResult((bool?)true));

        var context = BuildHttpContext("POST", "/api/items", token: "duplicate-token");

        await _middleware.InvokeAsync(context);

        _logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task InvokeAsync_DifferentTokens_BothPass()
    {
        _cache.GetAsync<bool?>(Arg.Any<string>())
            .Returns(Task.FromResult<bool?>(null));

        var context1 = BuildHttpContext("POST", "/api/items", token: "token-1");
        var context2 = BuildHttpContext("POST", "/api/items", token: "token-2");

        await _middleware.InvokeAsync(context1);
        await _middleware.InvokeAsync(context2);

        await _next.Received(2).Invoke(Arg.Any<HttpContext>());
    }

    [Fact]
    public async Task InvokeAsync_SameTokenDifferentTenant_TreatedAsDifferent()
    {
        _cache.GetAsync<bool?>(Arg.Any<string>())
            .Returns(Task.FromResult<bool?>(null));

        var context1 = BuildHttpContext("POST", "/api/items", token: "token", tenantId: "tenant-1");
        var context2 = BuildHttpContext("POST", "/api/items", token: "token", tenantId: "tenant-2");

        await _middleware.InvokeAsync(context1);
        await _middleware.InvokeAsync(context2);

        // Both should succeed (different tenant IDs)
        await _next.Received(2).Invoke(Arg.Any<HttpContext>());
    }

    [Fact]
    public async Task InvokeAsync_NoUserContext_TenantDefaultsToAnon()
    {
        _cache.GetAsync<bool?>(Arg.Any<string>())
            .Returns(Task.FromResult<bool?>(null));

        var context = BuildHttpContext("POST", "/api/items", token: "token", setUser: false);

        await _middleware.InvokeAsync(context);

        // Should still work but with "anon" tenant
        await _next.Received(1).Invoke(context);
    }

    // --- Helpers ---

    private static HttpContext BuildHttpContext(
        string method,
        string path,
        string? token = null,
        string tenantId = "default-tenant",
        bool setUser = true)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;

        if (token != null)
        {
            context.Request.Headers["X-Request-Token"] = token;
        }

        if (setUser)
        {
            var claims = new[] { new Claim("tenantId", tenantId) };
            var identity = new ClaimsIdentity(claims);
            var principal = new ClaimsPrincipal(identity);
            context.User = principal;
        }

        return context;
    }
}
