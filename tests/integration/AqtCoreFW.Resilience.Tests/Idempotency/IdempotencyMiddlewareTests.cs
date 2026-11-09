using System.Security.Claims;
using AqtCoreFW.Infrastructure.Middleware;
using AqtCoreFW.SharedKernel.Application.Caching;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Resilience.Tests.Idempotency;

/// <summary>
/// Unit tests for IdempotencyMiddleware.
/// Verifies: replay from cache, normal flow, and passthrough when no header.
/// </summary>
[Trait("Category", "Resilience")]
public sealed class IdempotencyMiddlewareTests
{
    private readonly ICacheService _cache = Substitute.For<ICacheService>();
    private readonly NullLogger<IdempotencyMiddleware> _logger = new();

    // ── helpers ────────────────────────────────────────────────────────────────

    private IdempotencyMiddleware BuildMiddleware(RequestDelegate next) =>
        new(next: next, cache: _cache, logger: _logger);

    private static DefaultHttpContext BuildPostContext(
        string? idempotencyKey = null,
        string tenantId = "tenant-1")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = HttpMethods.Post;

        if (idempotencyKey is not null)
            ctx.Request.Headers["X-Idempotency-Key"] = idempotencyKey;

        if (tenantId is not null)
        {
            ctx.User = new ClaimsPrincipal(
                new ClaimsIdentity([new Claim("tenantId", tenantId)]));
        }

        // Provide a writable response body stream
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    // ── TC-RES-IDP-001 ─────────────────────────────────────────────────────────

    /// <summary>TC-RES-IDP-001: Same idempotency key returns cached response.</summary>
    [Fact]
    public async Task SameIdempotencyKey_ReturnsCachedResponse()
    {
        // Arrange
        const string key = "req-abc-123";
        const int cachedStatus = 201;
        const string cachedBody = "{\"id\":\"42\"}";

        _cache.GetAsync<CachedIdempotencyResponse>(Arg.Any<string>())
              .Returns(new CachedIdempotencyResponse(cachedStatus, cachedBody));

        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };

        var middleware = BuildMiddleware(next);
        var ctx = BuildPostContext(idempotencyKey: key);

        // Act
        await middleware.InvokeAsync(ctx);

        // Assert
        nextCalled.Should().BeFalse("cached response should short-circuit the pipeline");
        ctx.Response.StatusCode.Should().Be(cachedStatus);
        nextCalled.Should().BeFalse();
    }

    // ── TC-RES-IDP-002 ─────────────────────────────────────────────────────────

    /// <summary>TC-RES-IDP-002: Different idempotency key processes normally and caches result.</summary>
    [Fact]
    public async Task DifferentIdempotencyKey_ProcessesNormallyAndCaches()
    {
        // Arrange — cache miss
        _cache.GetAsync<CachedIdempotencyResponse>(Arg.Any<string>())
              .Returns((CachedIdempotencyResponse?)null);

        var nextCalled = false;
        RequestDelegate next = ctx =>
        {
            nextCalled = true;
            ctx.Response.StatusCode = 200;
            return ctx.Response.WriteAsync("{\"ok\":true}");
        };

        var middleware = BuildMiddleware(next);
        var ctx = BuildPostContext(idempotencyKey: "new-unique-key-999");

        // Act
        await middleware.InvokeAsync(ctx);

        // Assert
        nextCalled.Should().BeTrue("new key should invoke the downstream handler");
        await _cache.Received(1)
                    .SetAsync(Arg.Any<string>(),
                              Arg.Any<CachedIdempotencyResponse>(),
                              Arg.Any<TimeSpan?>(),
                              Arg.Any<CancellationToken>());
    }

    // ── TC-RES-IDP-003 ─────────────────────────────────────────────────────────

    /// <summary>TC-RES-IDP-003: No idempotency header proceeds without any cache interaction.</summary>
    [Fact]
    public async Task NoIdempotencyHeader_ProceedsWithoutCaching()
    {
        // Arrange — no X-Idempotency-Key header
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };

        var middleware = BuildMiddleware(next);
        var ctx = BuildPostContext(idempotencyKey: null);

        // Act
        await middleware.InvokeAsync(ctx);

        // Assert
        nextCalled.Should().BeTrue();
        await _cache.DidNotReceive()
                    .GetAsync<CachedIdempotencyResponse>(Arg.Any<string>());
        await _cache.DidNotReceive()
                    .SetAsync(Arg.Any<string>(),
                              Arg.Any<CachedIdempotencyResponse>(),
                              Arg.Any<TimeSpan?>(),
                              Arg.Any<CancellationToken>());
    }
}
