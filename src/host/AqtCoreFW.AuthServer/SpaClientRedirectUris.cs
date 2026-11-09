namespace AqtCoreFW.AuthServer;

/// <summary>
/// Resolves the SPA client's redirect / post-logout-redirect URIs from configuration.
/// <para>
/// Development falls back to the localhost set below so the standard dev loop
/// (<c>scripts/1_run-local-dev</c>, the <c>.http</c> files, Playwright e2e, Swagger) needs no
/// configuration. Outside Development the URIs are REQUIRED: the seeder runs in every
/// environment, so a deploy without them would silently register a client whose only valid
/// callbacks are localhost — every authorize round-trip would then fail redirect_uri mismatch.
/// Mirrors the fail-fast posture of <see cref="Program.LoadRequiredCertificate"/>.
/// </para>
/// <para>
/// Entries that are null, empty, or whitespace are discarded before the "is it configured"
/// decision, so a committed-but-blank config value fails the same way an absent one does.
/// A malformed entry always throws, Development included — a typo in a tunnel URL should be
/// loud rather than silently dropped from the whitelist.
/// </para>
/// <para>
/// The <c>/silent-renew.html</c> entries are load-bearing; see the comment on
/// <see cref="DevelopmentRedirectUris"/>.
/// </para>
/// </summary>
public static class SpaClientRedirectUris
{
    public const string RedirectUrisKey = "Auth:SpaClient:RedirectUris";
    public const string PostLogoutRedirectUrisKey = "Auth:SpaClient:PostLogoutRedirectUris";

    /// <summary>
    /// Development-only fallback. The <c>/silent-renew.html</c> entries are used by
    /// oidc-client-ts when the refresh-token grant fails or expires. They MUST be whitelisted;
    /// a mismatch yields 400 invalid_request (per OAuth 2.0 §4.1.2.1 — the server MUST NOT
    /// redirect to unverified URIs), which oidc-client-ts surfaces as a silent-renew error in
    /// the iframe console even though the user-visible signinRedirect path uses /callback.
    /// </summary>
    public static readonly string[] DevelopmentRedirectUris =
    [
        "http://localhost:3000/callback",
        "http://localhost:4200/callback",
        "http://localhost:5173/callback",
        "http://127.0.0.1:5173/callback",
        "https://localhost:5001/swagger/oauth2-redirect.html",
        "http://localhost:3000/silent-renew.html",
        "http://localhost:4200/silent-renew.html",
        "http://localhost:5173/silent-renew.html",
        "http://127.0.0.1:5173/silent-renew.html",
    ];

    /// <summary>
    /// Development-only fallback. ORDER IS LOAD-BEARING: list each origin slashless FIRST.
    /// <para>
    /// <c>new Uri("http://h:p")</c> equals <c>new Uri("http://h:p/")</c>, so the descriptor's
    /// <see cref="ISet{T}"/> keeps only the FIRST spelling inserted — and OpenIddict persists
    /// <c>Uri.OriginalString</c>, then matches logout requests with an ordinal string compare
    /// (the relaxed URI-parsing path applies to native clients only; this client is Public).
    /// oidc-client-ts sends <c>window.location.origin</c>, which is always slashless, so a
    /// slash-terminated stored value fails with 400 invalid_request.
    /// </para>
    /// </summary>
    public static readonly string[] DevelopmentPostLogoutRedirectUris =
    [
        "http://localhost:3000",
        "http://localhost:3000/",
        "http://localhost:4200",
        "http://localhost:4200/",
        "http://localhost:5173",
        "http://localhost:5173/",
        "http://127.0.0.1:5173",
        "http://127.0.0.1:5173/",
    ];

    /// <summary>
    /// Reads <paramref name="configKey"/> as a string array, discards blank entries, and maps the
    /// remainder to absolute URIs. Falls back to <paramref name="developmentFallback"/> only when
    /// nothing usable is configured AND <paramref name="isDevelopment"/> is true.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Nothing configured outside Development, or an entry that is not an absolute http(s) URI.
    /// </exception>
    public static Uri[] Resolve(
        IConfiguration configuration,
        string configKey,
        string[] developmentFallback,
        bool isDevelopment)
    {
        // Blank-filter BEFORE the configured/absent decision: committed configs ship values as
        // empty strings, so `?? fallback` alone would treat "" as a deliberate choice.
        var values = configuration.GetSection(configKey).Get<string[]>()
            ?.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToArray() ?? [];

        if (values.Length == 0)
        {
            if (!isDevelopment)
            {
                throw new InvalidOperationException(
                    $"SPA client redirect URIs not configured: set {configKey} to the deployment's " +
                    "absolute callback URLs (e.g. https://app.example.gov.vn/callback). Required " +
                    "outside Development — the OpenIddict SPA client would otherwise be seeded with " +
                    "localhost-only URIs and every authorize round-trip would fail redirect_uri mismatch.");
            }

            values = developmentFallback;
        }

        var uris = new Uri[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            // Require an absolute http/https URI. The scheme check is load-bearing across platforms:
            // on Linux a relative value like "/callback" parses as an absolute file:///callback URI, so
            // UriKind.Absolute alone would let a misconfigured relative redirect slip through as a file://
            // URI instead of being rejected loudly. Every OIDC SPA redirect/post-logout URI is http(s).
            if (!Uri.TryCreate(values[i], UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException(
                    $"Invalid SPA client redirect URI in {configKey}: '{values[i]}' is not an absolute http(s) URI.");
            }

            uris[i] = uri;
        }

        return uris;
    }
}
