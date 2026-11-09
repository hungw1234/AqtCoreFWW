using System.Collections.Concurrent;
using AqtCoreFW.Infrastructure.Clients;
using AqtCoreFW.Infrastructure.Security;
using AqtCoreFW.Organization.Infrastructure;
using AqtCoreFW.Organization.Infrastructure.Clients;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Organization.Integration.Tests.Clients;

/// <summary>
/// Drives <see cref="HttpOrgUnitModuleClient"/> through the REAL registration path
/// (<c>AddHttpOrgUnitModuleClient</c>) against an in-memory <c>TestServer</c> stub — only the socket is
/// swapped, so <see cref="TenantPropagationHandler"/> is exercised for real. Proves per-method
/// serialization round-trips, tenant-header propagation, the DI transport toggle, and the client-side
/// batch-cap / short-circuit guards. DB-free (no <c>SqlServerCollection</c>).
/// </summary>
public sealed class HttpOrgUnitModuleClientTests
{
    // ── Round-trip (one per contract method) ─────────────────────────────────

    [Fact]
    public async Task GetOrgUnitsByIds_RoundTrips_KeyedById_AndSendsIds()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        await using var h = await StartHarnessAsync(tenant, s => s.ByIds =
        [
            new OrgUnitInfoDto { Id = id1, Name = "Alpha" },
            new OrgUnitInfoDto { Id = id2, Name = "Beta" },
        ]);

        var result = await h.Client.GetOrgUnitsByIdsAsync([id1, id2], tenant);

        result.Should().HaveCount(2);
        result[id1].Name.Should().Be("Alpha");
        result[id2].Name.Should().Be("Beta");
        result.Keys.Should().BeEquivalentTo(new[] { id1, id2 });
        // Request-body serialization is real, not vacuous — the stub deserialized these ids.
        h.State.LastByIdsRequestIds.Should().BeEquivalentTo(new[] { id1, id2 });
    }

    [Fact]
    public async Task GetRootOrgUnitForTenant_RoundTrips_NonNull()
    {
        var tenant = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        await using var h = await StartHarnessAsync(tenant, s => s.RootForTenant =
            new OrgUnitSummary { TenantId = tenant, Id = rootId, Name = "Root" });

        var result = await h.Client.GetRootOrgUnitForTenantAsync(tenant);

        result.Should().NotBeNull();
        result!.Id.Should().Be(rootId);
        result.TenantId.Should().Be(tenant);
        result.Name.Should().Be("Root");
    }

    [Fact]
    public async Task GetRootOrgUnitForTenant_NullResponse_ReturnsNull()
    {
        await using var h = await StartHarnessAsync(Guid.NewGuid(), s => s.RootForTenant = null);

        var result = await h.Client.GetRootOrgUnitForTenantAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetRootOrgUnitsForTenants_RoundTrips_KeyedByTenantId()
    {
        var tA = Guid.NewGuid();
        var tB = Guid.NewGuid();
        await using var h = await StartHarnessAsync(Guid.NewGuid(), s => s.RootsForTenants =
        [
            new OrgUnitSummary { TenantId = tA, Id = Guid.NewGuid(), Name = "RA" },
            new OrgUnitSummary { TenantId = tB, Id = Guid.NewGuid(), Name = "RB" },
        ]);

        var result = await h.Client.GetRootOrgUnitsForTenantsAsync([tA, tB]);

        result.Keys.Should().BeEquivalentTo(new[] { tA, tB });
        result[tA].Name.Should().Be("RA");
        result[tB].Name.Should().Be("RB");
    }

    [Fact]
    public async Task ExpandSubtree_RoundTrips_PreservesOrderAndCompleteness()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        await using var h = await StartHarnessAsync(Guid.NewGuid(), s => s.ExpandSubtree = ids);

        var result = await h.Client.ExpandSubtreeAsync(Guid.NewGuid(), Guid.NewGuid());

        result.Should().Equal(ids);
    }

    [Fact]
    public async Task ResolveActiveUsersInOrgUnits_RoundTrips_AndSendsCodes()
    {
        var users = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var codes = new[] { "HR", "IT" };
        await using var h = await StartHarnessAsync(Guid.NewGuid(), s => s.ResolveActiveUsers = users);

        var result = await h.Client.ResolveActiveUsersInOrgUnitsAsync(codes, Guid.NewGuid());

        result.Should().Equal(users);
        h.State.LastResolveCodes.Should().BeEquivalentTo(codes);
    }

    [Fact]
    public async Task GetUserOrgUnitCodes_RoundTrips_AsOrdinalSet()
    {
        var codes = new[] { "HR", "IT", "FINANCE" };
        await using var h = await StartHarnessAsync(Guid.NewGuid(), s => s.UserCodes = codes);

        var result = await h.Client.GetUserOrgUnitCodesAsync(Guid.NewGuid(), Guid.NewGuid());

        result.Should().BeEquivalentTo(codes);
        result.Contains("HR").Should().BeTrue();
        result.Contains("hr").Should().BeFalse();   // Ordinal comparer is case-sensitive
    }

    // ── Header propagation (TenantPropagationHandler, exercised for real) ─────

    [Fact]
    public async Task TenantPropagation_SetsTenantHeader_WhenAmbientTenantPresent()
    {
        var tenant = Guid.NewGuid();
        await using var h = await StartHarnessAsync(tenant);

        await h.Client.GetOrgUnitsByIdsAsync([Guid.NewGuid()], tenant);

        h.State.TenantHeaderPresent.Should().BeTrue();
        h.State.LastTenantHeader.Should().Be(tenant.ToString());
    }

    [Fact]
    public async Task TenantPropagation_OmitsTenantHeader_WhenNoAmbientTenant()
    {
        // Ambient tenant is null even though the method arg carries a tenant — the header derives from
        // ITenantContext, not the arg. Non-vacuous negative.
        await using var h = await StartHarnessAsync(tenantId: null);

        await h.Client.GetOrgUnitsByIdsAsync([Guid.NewGuid()], tenantId: Guid.NewGuid());

        h.State.TenantHeaderPresent.Should().BeFalse();
    }

    // ── DI transport toggle (isolated, via the internal helper) ───────────────

    [Fact]
    public void Toggle_ConfigAbsent_ResolvesInProcessClient()
    {
        using var sp = BuildToggleProvider(transport: null, baseAddress: null);
        using var scope = sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<IOrgUnitModuleClient>()
            .Should().BeOfType<InProcessOrgUnitModuleClient>();
    }

    [Fact]
    public void Toggle_InProcess_ResolvesInProcessClient()
    {
        using var sp = BuildToggleProvider(transport: "InProcess", baseAddress: null);
        using var scope = sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<IOrgUnitModuleClient>()
            .Should().BeOfType<InProcessOrgUnitModuleClient>();
    }

    [Fact]
    public void Toggle_Http_ResolvesHttpClient()
    {
        using var sp = BuildToggleProvider(transport: "Http", baseAddress: "http://orgunit");
        using var scope = sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<IOrgUnitModuleClient>()
            .Should().BeOfType<HttpOrgUnitModuleClient>();
    }

    [Fact]
    public void Toggle_Http_WithoutBaseAddress_FailsFast()
    {
        var services = ToggleServices();
        var config = Config(transport: "Http", baseAddress: null);

        Action act = () => OrganizationInfrastructureRegistration.AddOrgUnitModuleClient(services, config);

        act.Should().Throw<InvalidOperationException>().WithMessage("*BaseAddress is required*");
    }

    // ── Guard / short-circuit mutation proofs (no HTTP call must be made) ──────

    [Fact]
    public async Task GetOrgUnitsByIds_OverCap_Throws_WithoutHttpCall()
    {
        await using var h = await StartHarnessAsync(Guid.NewGuid());
        var ids = Enumerable.Range(0, IOrgUnitModuleClient.MaxBatchSize + 1)
            .Select(_ => Guid.NewGuid()).ToArray();

        var act = () => h.Client.GetOrgUnitsByIdsAsync(ids);

        await act.Should().ThrowAsync<ArgumentException>();
        h.State.Hits.GetValueOrDefault("by-ids").Should().Be(0);
    }

    [Fact]
    public async Task GetRootOrgUnitsForTenants_OverCap_Throws_WithoutHttpCall()
    {
        await using var h = await StartHarnessAsync(Guid.NewGuid());
        var ids = Enumerable.Range(0, IOrgUnitModuleClient.MaxBatchSize + 1)
            .Select(_ => Guid.NewGuid()).ToArray();

        var act = () => h.Client.GetRootOrgUnitsForTenantsAsync(ids);

        await act.Should().ThrowAsync<ArgumentException>();
        h.State.Hits.GetValueOrDefault("roots-for-tenants").Should().Be(0);
    }

    [Fact]
    public async Task ResolveActiveUsers_EmptyCodes_ShortCircuits_NoHttpCall()
    {
        await using var h = await StartHarnessAsync(Guid.NewGuid());

        var result = await h.Client.ResolveActiveUsersInOrgUnitsAsync([], Guid.NewGuid());

        result.Should().BeEmpty();
        h.State.Hits.GetValueOrDefault("resolve-active-users").Should().Be(0);
    }

    [Fact]
    public async Task GetOrgUnitsByIds_EmptyInput_ShortCircuits_NoHttpCall()
    {
        await using var h = await StartHarnessAsync(Guid.NewGuid());

        var result = await h.Client.GetOrgUnitsByIdsAsync([]);

        result.Should().BeEmpty();
        h.State.Hits.GetValueOrDefault("by-ids").Should().Be(0);
    }

    // The two batch caps differ deliberately (mirroring InProcess): by-ids caps on the DISTINCT count,
    // roots-for-tenants on the RAW count. These two lock that divergence — an input whose raw count is
    // over the cap but distinct count is under it must behave oppositely per method.

    [Fact]
    public async Task GetOrgUnitsByIds_RawOverCapButDistinctUnderCap_CapsOnDistinct_CallsHttp()
    {
        await using var h = await StartHarnessAsync(Guid.NewGuid());
        var dup = Guid.NewGuid();
        var ids = Enumerable.Repeat(dup, IOrgUnitModuleClient.MaxBatchSize + 1).ToArray();

        var act = () => h.Client.GetOrgUnitsByIdsAsync(ids);

        // Distinct count is 1 → under the cap → the call proceeds (caps on DISTINCT, not raw).
        await act.Should().NotThrowAsync();
        h.State.Hits.GetValueOrDefault("by-ids").Should().Be(1);
    }

    [Fact]
    public async Task GetRootOrgUnitsForTenants_RawOverCapEvenIfDistinctUnderCap_CapsOnRaw_Throws()
    {
        await using var h = await StartHarnessAsync(Guid.NewGuid());
        var dup = Guid.NewGuid();
        var ids = Enumerable.Repeat(dup, IOrgUnitModuleClient.MaxBatchSize + 1).ToList();

        var act = () => h.Client.GetRootOrgUnitsForTenantsAsync(ids);

        // Raw count is over the cap → throws even though distinct is 1 (caps on RAW, not distinct).
        await act.Should().ThrowAsync<ArgumentException>();
        h.State.Hits.GetValueOrDefault("roots-for-tenants").Should().Be(0);
    }

    [Fact]
    public async Task GetRootOrgUnitsForTenants_EmptyInput_ShortCircuits_NoHttpCall()
    {
        await using var h = await StartHarnessAsync(Guid.NewGuid());

        var result = await h.Client.GetRootOrgUnitsForTenantsAsync([]);

        result.Should().BeEmpty();
        h.State.Hits.GetValueOrDefault("roots-for-tenants").Should().Be(0);
    }

    // ── Harness ───────────────────────────────────────────────────────────────

    private static async Task<Harness> StartHarnessAsync(Guid? tenantId, Action<StubState>? configure = null)
    {
        var state = new StubState();
        configure?.Invoke(state);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        var app = builder.Build();
        MapStubEndpoints(app, state);
        await app.StartAsync();
        var primaryHandler = app.GetTestServer().CreateHandler();

        var config = Config("Http", "http://orgunit-stub");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<ITenantContext>(new StubTenantContext { TenantId = tenantId });
        services.AddSingleton<IServiceIdentityProvider, NoOpServiceIdentityProvider>();
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddTransient<TenantPropagationHandler>();
        services.AddHttpOrgUnitModuleClient(config);                  // ← wiring under test
        services.AddHttpClient(nameof(HttpOrgUnitModuleClient))       // same named options bag
                .ConfigurePrimaryHttpMessageHandler(() => primaryHandler);   // last-wins swap to the stub
        var sp = services.BuildServiceProvider();

        return new Harness(app, sp, state, sp.GetRequiredService<IOrgUnitModuleClient>());
    }

    private static void MapStubEndpoints(WebApplication app, StubState s)
    {
        app.MapPost("/internal/org-units/by-ids", async (HttpContext ctx) =>
        {
            Record(ctx, s, "by-ids");
            var req = await ctx.Request.ReadFromJsonAsync<StubByIdsRequest>();
            s.LastByIdsRequestIds = req?.Ids?.ToArray();
            return Results.Json(s.ByIds);
        });
        app.MapPost("/internal/org-units/root-for-tenant", (HttpContext ctx) =>
        {
            Record(ctx, s, "root-for-tenant");
            return Results.Json(s.RootForTenant);
        });
        app.MapPost("/internal/org-units/roots-for-tenants", (HttpContext ctx) =>
        {
            Record(ctx, s, "roots-for-tenants");
            return Results.Json(s.RootsForTenants);
        });
        app.MapPost("/internal/org-units/expand-subtree", (HttpContext ctx) =>
        {
            Record(ctx, s, "expand-subtree");
            return Results.Json(s.ExpandSubtree);
        });
        app.MapPost("/internal/org-units/resolve-active-users", async (HttpContext ctx) =>
        {
            Record(ctx, s, "resolve-active-users");
            var req = await ctx.Request.ReadFromJsonAsync<StubResolveRequest>();
            s.LastResolveCodes = req?.Codes?.ToArray();
            return Results.Json(s.ResolveActiveUsers);
        });
        app.MapPost("/internal/org-units/user-codes", (HttpContext ctx) =>
        {
            Record(ctx, s, "user-codes");
            return Results.Json(s.UserCodes);
        });
    }

    private static void Record(HttpContext ctx, StubState s, string key)
    {
        s.Hits.AddOrUpdate(key, 1, (_, n) => n + 1);
        if (ctx.Request.Headers.TryGetValue("X-Tenant-Id", out var v))
        {
            s.TenantHeaderPresent = true;
            s.LastTenantHeader = v.ToString();
        }
        else
        {
            s.TenantHeaderPresent = false;
        }
    }

    private static ServiceProvider BuildToggleProvider(string? transport, string? baseAddress)
    {
        var services = ToggleServices();
        OrganizationInfrastructureRegistration.AddOrgUnitModuleClient(services, Config(transport, baseAddress));
        return services.BuildServiceProvider();
    }

    private static ServiceCollection ToggleServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // InProcess-branch deps (constructed, never queried — assertions only inspect runtime type).
        services.AddSingleton(Substitute.For<IReadDbConnection>());
        services.AddSingleton(TimeProvider.System);
        // Http-branch handler deps.
        services.AddSingleton<ITenantContext>(new StubTenantContext());
        services.AddSingleton<IServiceIdentityProvider, NoOpServiceIdentityProvider>();
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddTransient<TenantPropagationHandler>();
        return services;
    }

    private static IConfiguration Config(string? transport, string? baseAddress)
    {
        var dict = new Dictionary<string, string?>();
        if (transport is not null) dict["OrgUnitModuleClient:Transport"] = transport;
        if (baseAddress is not null) dict["OrgUnitModuleClient:BaseAddress"] = baseAddress;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private sealed class StubTenantContext : ITenantContext
    {
        public Guid? TenantId { get; set; }
        public bool IsSystemAdmin { get; set; }
    }

    private sealed class StubState
    {
        public readonly ConcurrentDictionary<string, int> Hits = new();
        public string? LastTenantHeader;
        public bool TenantHeaderPresent;

        public OrgUnitInfoDto[] ByIds = [];
        public OrgUnitSummary? RootForTenant;
        public OrgUnitSummary[] RootsForTenants = [];
        public Guid[] ExpandSubtree = [];
        public Guid[] ResolveActiveUsers = [];
        public string[] UserCodes = [];

        public Guid[]? LastByIdsRequestIds;
        public string[]? LastResolveCodes;
    }

    // Stub-local wire records — independent of the client's private records; agreement is proven at the
    // JSON boundary, not by a shared object graph.
    private sealed record StubByIdsRequest(List<Guid> Ids, Guid? TenantId);
    private sealed record StubResolveRequest(List<string> Codes, Guid TenantId);

    private sealed class Harness(
        WebApplication app, ServiceProvider sp, StubState state, IOrgUnitModuleClient client) : IAsyncDisposable
    {
        public StubState State => state;
        public IOrgUnitModuleClient Client => client;

        public async ValueTask DisposeAsync()
        {
            await sp.DisposeAsync();
            await app.DisposeAsync();
        }
    }
}
