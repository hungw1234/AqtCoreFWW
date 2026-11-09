namespace AqtCoreFW.Identity.Infrastructure.VNeID;

/// <summary>
/// Development stub for VNeID connector — always returns a fixed test identity.
/// Replace with VNeIdHttpConnector in production pointing to actual VNeID gateway.
/// </summary>
public sealed class VNeIdStubConnector : IVneIdConnector
{
    public Task<VNeIdUserInfo?> GetUserInfoAsync(string authCode, CancellationToken ct = default)
    {
        // Stub returns a deterministic test citizen based on the auth code prefix.
        var info = new VNeIdUserInfo(
            Sub: $"vne_{authCode[..Math.Min(8, authCode.Length)]}",
            FullName: "Nguyễn Văn Test",
            Cccd: "001234567890",
            EkycLevel: 2);

        return Task.FromResult<VNeIdUserInfo?>(info);
    }
}
