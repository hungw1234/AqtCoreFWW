namespace AqtCoreFW.Identity.Infrastructure.VNeID;

/// <summary>
/// Connector interface for VNeID OIDC external identity provider (NĐ59).
/// In production: OAuth2 Authorization Code exchange via VNeID gateway.
/// </summary>
public interface IVneIdConnector
{
    Task<VNeIdUserInfo?> GetUserInfoAsync(string authCode, CancellationToken ct = default);
}

/// <summary>VNeID user info returned after successful OIDC exchange.</summary>
public sealed record VNeIdUserInfo(
    string Sub,
    string FullName,
    string? Cccd,
    int EkycLevel);
