using System.Net;
using System.Text;
using AqtCoreFW.Forms.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VaultSharp;
using VaultSharp.V1;
using VaultSharp.V1.SecretsEngines;
using VaultSharp.V1.SecretsEngines.KeyValue;
using VaultSharp.V1.SecretsEngines.KeyValue.V2;
using VaultSharp.V1.Commons;

namespace AqtCoreFW.Forms.Infrastructure.Tests.Services;

/// <summary>
/// H2 — authenticated external data source. The resolver reads a per-source API key from Vault and attaches
/// it as a request header; a Vault miss / unwired Vault fails closed (empty options); the static path
/// (AuthMode null) never touches Vault; and the SSRF guards still apply to the authed URL.
///
/// The tests drive the REAL <see cref="ExternalDataSourceResolver.ResolveAsync"/> through its public JSON
/// contract, with a stub <see cref="HttpMessageHandler"/> that captures the outbound request so the auth
/// header can be asserted. IVaultClient is an NSubstitute mock (or null, for the fail-closed cases).
/// </summary>
public sealed class ExternalDataSourceAuthResolverTests
{
    private static readonly Guid Tenant = Guid.Parse("22222222-2222-2222-2222-222222222222");
    // Public IP-literal host: IsAllowedUrl passes (not RFC-1918) and IsPrivateAfterResolutionAsync
    // short-circuits for IP literals — so the tests never depend on live DNS for api.example.com.
    private const string ApiUrl = "https://93.184.216.34/wards";

    // ---- Stub HttpMessageHandler: captures the request, returns a fixed JSON array body ----
    private sealed class CapturingHandler(string responseJson) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static IHttpClientFactory FactoryFor(HttpMessageHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler));
        return factory;
    }

    // IDistributedCache that always misses on Get and no-ops on Set — forces the HTTP path every call.
    private static IDistributedCache MissingCache()
    {
        var cache = Substitute.For<IDistributedCache>();
        cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((byte[]?)null);
        return cache;
    }

    private static IVaultClient VaultReturning(string keyValue, out IKeyValueSecretsEngineV2 v2Out)
    {
        var vault = Substitute.For<IVaultClient>();
        var v1 = Substitute.For<IVaultClientV1>();
        var engines = Substitute.For<ISecretsEngine>();
        var kv = Substitute.For<IKeyValueSecretsEngine>();
        var v2 = Substitute.For<IKeyValueSecretsEngineV2>();

        vault.V1.Returns(v1);
        v1.Secrets.Returns(engines);
        engines.KeyValue.Returns(kv);
        kv.V2.Returns(v2);

        var secret = new Secret<SecretData>
        {
            Data = new SecretData { Data = new Dictionary<string, object> { ["value"] = keyValue } },
        };
        v2.ReadSecretAsync(
                Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(secret);
        v2Out = v2;
        return vault;
    }

    private static IVaultClient VaultReturning(string keyValue) => VaultReturning(keyValue, out _);

    private static ExternalDataSourceAuthKeyResolver KeyResolver(IVaultClient? vault) =>
        new(new MemoryCache(new MemoryCacheOptions()),
            NullLogger<ExternalDataSourceAuthKeyResolver>.Instance,
            vault);

    private static string Config(string? authMode = null, string? vaultKeyId = null, string? headerName = null,
        string apiUrl = ApiUrl)
    {
        // Minimal DataSourceJson; auth fields omitted when null (mirrors a static-source config).
        var sb = new StringBuilder();
        sb.Append($"{{\"ApiUrl\":\"{apiUrl}\",\"LabelJsonPath\":\"name\",\"ValueJsonPath\":\"id\"");
        if (authMode is not null) sb.Append($",\"AuthMode\":\"{authMode}\"");
        if (vaultKeyId is not null) sb.Append($",\"VaultKeyId\":\"{vaultKeyId}\"");
        if (headerName is not null) sb.Append($",\"HeaderName\":\"{headerName}\"");
        sb.Append('}');
        return sb.ToString();
    }

    private const string ArrayBody = """[{"id":"1","name":"Alpha"},{"id":"2","name":"Beta"}]""";

    [Fact]
    public async Task StaticSource_AuthModeNull_DoesNotReadVault_AndReturnsOptions()
    {
        var handler = new CapturingHandler(ArrayBody);
        var vault = Substitute.For<IVaultClient>();

        var resolver = new ExternalDataSourceResolver(FactoryFor(handler), MissingCache(), KeyResolver(vault));
        var result = await resolver.ResolveAsync(Config(authMode: null), Tenant);

        result.Should().HaveCount(2, "static path resolves options unchanged (backward)");
        handler.LastRequest!.Headers.Authorization.Should().BeNull("no auth header on the static path");
        // Vault must never be touched on the static path.
        _ = vault.DidNotReceive().V1;
    }

    [Fact]
    public async Task BearerAuth_ReadsVaultKey_AndSetsAuthorizationBearerHeader()
    {
        var handler = new CapturingHandler(ArrayBody);
        var resolver = new ExternalDataSourceResolver(
            FactoryFor(handler), MissingCache(), KeyResolver(VaultReturning("secret-key-123")));

        var result = await resolver.ResolveAsync(
            Config(authMode: "Bearer", vaultKeyId: "ds-42"), Tenant);

        result.Should().HaveCount(2);
        handler.LastRequest!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.Should().Be("secret-key-123");
    }

    [Fact]
    public async Task Authed_ReadsVaultAtServerSidePathForTheConfiguredKeyId()
    {
        var handler = new CapturingHandler(ArrayBody);
        var vault = VaultReturning("k", out var v2);
        var resolver = new ExternalDataSourceResolver(FactoryFor(handler), MissingCache(), KeyResolver(vault));

        await resolver.ResolveAsync(Config(authMode: "Bearer", vaultKeyId: "ds-99"), Tenant);

        // Pin the exact Vault path template + mount so a future edit to either can't silently pass.
        await v2.Received(1).ReadSecretAsync(
            path: "aqt/forms/external-ds/ds-99/api-key",
            version: Arg.Any<int?>(),
            mountPoint: "secret",
            wrapTimeToLive: Arg.Any<string>());
    }

    [Fact]
    public async Task ApiKeyHeaderAuth_SetsCustomHeaderName()
    {
        var handler = new CapturingHandler(ArrayBody);
        var resolver = new ExternalDataSourceResolver(
            FactoryFor(handler), MissingCache(), KeyResolver(VaultReturning("abc-key")));

        var result = await resolver.ResolveAsync(
            Config(authMode: "ApiKeyHeader", vaultKeyId: "ds-7", headerName: "X-Api-Key"), Tenant);

        result.Should().HaveCount(2);
        handler.LastRequest!.Headers.GetValues("X-Api-Key").Should().ContainSingle().Which.Should().Be("abc-key");
    }

    [Fact]
    public async Task AuthedButVaultUnwired_FailsClosed_ReturnsEmpty_AndDoesNotCallApi()
    {
        var handler = new CapturingHandler(ArrayBody);
        // vault = null ⇒ key resolver returns null ⇒ authed resolve fails closed BEFORE any HTTP call.
        var resolver = new ExternalDataSourceResolver(FactoryFor(handler), MissingCache(), KeyResolver(vault: null));

        var result = await resolver.ResolveAsync(
            Config(authMode: "Bearer", vaultKeyId: "ds-42"), Tenant);

        result.Should().BeEmpty("no key ⇒ fail closed, never fetch unauthenticated");
        handler.LastRequest.Should().BeNull("the API is never called when the key is unavailable");
    }

    [Fact]
    public async Task AuthedUrl_ToPrivateRange_StillBlockedBySsrfGuard()
    {
        var handler = new CapturingHandler(ArrayBody);
        var resolver = new ExternalDataSourceResolver(
            FactoryFor(handler), MissingCache(), KeyResolver(VaultReturning("k")));

        // A private-range host must be rejected by the SSRF guard even on the authed path.
        var result = await resolver.ResolveAsync(
            Config(authMode: "Bearer", vaultKeyId: "ds-42", apiUrl: "https://10.0.0.5/wards"), Tenant);

        result.Should().BeEmpty("SSRF guard applies to the authed URL too");
        handler.LastRequest.Should().BeNull("blocked before any HTTP call");
    }
}
