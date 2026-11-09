using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AqtCoreFW.Identity.Application.Signing;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing;

/// <summary>
/// HS256 JWT service for VGCA Tier-3 doc-flow short-lived tokens (W3, QĐ 742 mục 5.1).
///
/// <para>Claims layout:
/// <list type="bullet">
///   <item><term>sub</term><description>userId (Guid string)</description></item>
///   <item><term>docId</term><description>Guid string — document being signed</description></item>
///   <item><term>nonce</term><description>Guid string — replay-prevention nonce (FK to SigningDocFlowNonce)</description></item>
///   <item><term>tenant_id</term><description>Guid string — owning tenant; asserted by the [AllowAnonymous] handler</description></item>
///   <item><term>iss</term><description><see cref="DocFlowJwtOptions.IssuerName"/></description></item>
///   <item><term>exp</term><description>Unix timestamp (now + <see cref="DocFlowJwtOptions.TtlSeconds"/>)</description></item>
/// </list>
/// </para>
///
/// <para>Key: HMAC-SHA256 over <see cref="DocFlowJwtOptions.SigningKey"/> (UTF-8 encoded).
/// In production the key is sourced from Vault (path <c>aqt/signing/docflow-jwt/key</c>);
/// in development it is set in appsettings.Development.json.</para>
/// </summary>
public sealed class DocFlowJwtService(IOptions<DocFlowJwtOptions> options) : IDocFlowJwtService
{
    // MapInboundClaims defaults to true, which rewrites the standard "sub" claim to
    // ClaimTypes.NameIdentifier during ValidateToken — then FindFirstValue(JwtRegisteredClaimNames.Sub)
    // below reads null and the sub claim is reported "missing or invalid", failing every download/upload.
    // Disable the remap so the validated principal keeps the raw JWT claim names the token was issued
    // with. (doc_id/nonce/tenant_id are custom names absent from the inbound map, so only sub was hit.)
    private static readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };

    // Custom claim names — distinct from standard OIDC claims to avoid ambiguity
    private const string ClaimDocId  = "doc_id";
    private const string ClaimNonce  = "nonce";
    private const string ClaimTenant = "tenant_id";

    /// <inheritdoc/>
    public string IssueJwt(Guid userId, Guid docId, Guid nonce, Guid tenantId)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.SigningKey) || opts.SigningKey.Length < 16)
            throw new InvalidOperationException(
                "Signing:DocFlowJwt:SigningKey is not configured or too short (minimum 16 chars).");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub,  userId.ToString()),
                new Claim(ClaimDocId,                   docId.ToString()),
                new Claim(ClaimNonce,                   nonce.ToString()),
                new Claim(ClaimTenant,                  tenantId.ToString()),
            ]),
            Issuer             = opts.IssuerName,
            IssuedAt           = now,
            Expires            = now.AddSeconds(opts.TtlSeconds),
            SigningCredentials = creds,
        };

        var token = _handler.CreateJwtSecurityToken(descriptor);
        return _handler.WriteToken(token);
    }

    /// <inheritdoc/>
    public bool TryValidate(
        string jwt,
        out Guid userId,
        out Guid docId,
        out Guid nonce,
        out Guid tenantId,
        out string? error)
    {
        userId   = Guid.Empty;
        docId    = Guid.Empty;
        nonce    = Guid.Empty;
        tenantId = Guid.Empty;
        error    = null;

        if (string.IsNullOrWhiteSpace(jwt))
        {
            error = "Token is missing.";
            return false;
        }

        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.SigningKey))
        {
            error = "JWT signing key is not configured.";
            return false;
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.SigningKey));

        var parameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = key,
            ValidateIssuer           = true,
            ValidIssuer              = opts.IssuerName,
            ValidateAudience         = false, // no audience claim — doc-flow tokens are handler-internal
            ValidateLifetime         = true,
            ClockSkew                = TimeSpan.FromSeconds(30),
        };

        ClaimsPrincipal principal;
        try
        {
            principal = _handler.ValidateToken(jwt, parameters, out _);
        }
        catch (SecurityTokenExpiredException)
        {
            error = "Token has expired.";
            return false;
        }
        catch (SecurityTokenException ex)
        {
            error = $"Token validation failed: {ex.Message}";
            return false;
        }

        // Extract and parse required claims
        var subClaim    = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var docIdClaim  = principal.FindFirstValue(ClaimDocId);
        var nonceClaim  = principal.FindFirstValue(ClaimNonce);
        var tenantClaim = principal.FindFirstValue(ClaimTenant);

        if (!Guid.TryParse(subClaim, out userId))
        {
            error = "Token sub claim is missing or invalid.";
            return false;
        }
        if (!Guid.TryParse(docIdClaim, out docId))
        {
            error = "Token doc_id claim is missing or invalid.";
            return false;
        }
        if (!Guid.TryParse(nonceClaim, out nonce))
        {
            error = "Token nonce claim is missing or invalid.";
            return false;
        }
        if (!Guid.TryParse(tenantClaim, out tenantId))
        {
            error = "Token tenant_id claim is missing or invalid.";
            return false;
        }

        return true;
    }
}
