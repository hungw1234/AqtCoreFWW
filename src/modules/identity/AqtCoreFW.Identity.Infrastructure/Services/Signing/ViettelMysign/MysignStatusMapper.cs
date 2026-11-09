namespace AqtCoreFW.Identity.Infrastructure.Services.Signing.ViettelMysign;

/// <summary>
/// Result of mapping a Viettel Mysign status code to a strongly-typed representation.
/// </summary>
/// <param name="IsTerminal">True when this status is a final outcome — no further polling needed.</param>
/// <param name="IsSuccess">True only when signing completed successfully (status="1").</param>
/// <param name="ReadableMessage">Human-readable description for logging / failure reasons.</param>
public readonly record struct MysignStatusInfo(bool IsTerminal, bool IsSuccess, string ReadableMessage);

/// <summary>
/// Maps Viettel Mysign polling status codes to strongly-typed <see cref="MysignStatusInfo"/>.
///
/// Status codes sourced from Viettel Mysign spec pp.27-43 + status table p.43 / p.1769:
/// <list type="table">
///   <listheader><term>Code</term><term>Terminal</term><term>Success</term><description>Meaning</description></listheader>
///   <item><term>"1"</term>    <term>yes</term><term>yes</term><description>Signing completed successfully.</description></item>
///   <item><term>"4000"</term> <term>no</term> <term>-</term> <description>Waiting for user authorisation on mobile app.</description></item>
///   <item><term>"6000"</term> <term>no</term> <term>-</term> <description>User confirmed — signature generation in progress.</description></item>
///   <item><term>"4001"</term> <term>yes</term><term>no</term><description>Request timed out at provider.</description></item>
///   <item><term>"4002"</term> <term>yes</term><term>no</term><description>User explicitly rejected the request.</description></item>
///   <item><term>"4003"</term> <term>no</term> <term>-</term> <description>Waiting for signature generation (CA processing).</description></item>
///   <item><term>"4004"</term> <term>yes</term><term>no</term><description>Signing failed — internal provider error.</description></item>
///   <item><term>"4005"</term> <term>yes</term><term>no</term><description>Account balance / signing quota exhausted.</description></item>
///   <item><term>"4006"</term> <term>yes</term><term>no</term><description>Certificate not yet accepted / activated.</description></item>
///   <item><term>"4007"</term> <term>no</term> <term>-</term> <description>Waiting for signature to be merged into document.</description></item>
///   <item><term>"4008"</term> <term>yes</term><term>no</term><description>Invalid subscriber — user not authorised for this operation.</description></item>
///   <item><term>"13003"</term><term>yes</term><term>no</term><description>Certificate has expired.</description></item>
///   <item><term>"13004"</term><term>yes</term><term>no</term><description>Certificate is invalid or has been revoked.</description></item>
///   <item><term>"50000"</term><term>yes</term><term>no</term><description>Internal provider error retrieving status.</description></item>
/// </list>
/// Unknown codes are treated as terminal failures with the raw code in the message (ops alert surface).
/// Null status is treated as terminal failure (defensive — Viettel always returns status on poll).
/// </summary>
public static class MysignStatusMapper
{
    /// <summary>
    /// Maps a Viettel Mysign status code string to a <see cref="MysignStatusInfo"/>.
    /// Never throws — unknown/null codes produce a terminal-failure result.
    /// </summary>
    public static MysignStatusInfo Map(string? code) => code switch
    {
        "1"     => new(IsTerminal: true,  IsSuccess: true,  ReadableMessage: "Signing completed successfully"),
        "4000"  => new(IsTerminal: false, IsSuccess: false, ReadableMessage: "Waiting for user authorisation on mobile app"),
        "6000"  => new(IsTerminal: false, IsSuccess: false, ReadableMessage: "User confirmed — signature generation in progress"),
        "4001"  => new(IsTerminal: true,  IsSuccess: false, ReadableMessage: "Request timed out at provider"),
        "4002"  => new(IsTerminal: true,  IsSuccess: false, ReadableMessage: "User rejected the signing request"),
        "4003"  => new(IsTerminal: false, IsSuccess: false, ReadableMessage: "Waiting for signature generation (CA processing)"),
        "4004"  => new(IsTerminal: true,  IsSuccess: false, ReadableMessage: "Signing failed — internal provider error"),
        "4005"  => new(IsTerminal: true,  IsSuccess: false, ReadableMessage: "Account balance or signing quota exhausted"),
        "4006"  => new(IsTerminal: true,  IsSuccess: false, ReadableMessage: "Certificate not yet accepted or activated"),
        "4007"  => new(IsTerminal: false, IsSuccess: false, ReadableMessage: "Waiting for signature to be merged into document"),
        "4008"  => new(IsTerminal: true,  IsSuccess: false, ReadableMessage: "Invalid subscriber — user not authorised for this operation"),
        "13003" => new(IsTerminal: true,  IsSuccess: false, ReadableMessage: "Certificate has expired"),
        "13004" => new(IsTerminal: true,  IsSuccess: false, ReadableMessage: "Certificate is invalid or has been revoked"),
        "50000" => new(IsTerminal: true,  IsSuccess: false, ReadableMessage: "Internal provider error retrieving status"),
        null    => new(IsTerminal: true,  IsSuccess: false, ReadableMessage: "Status code missing in response — treating as terminal failure"),
        _       => new(IsTerminal: true,  IsSuccess: false, ReadableMessage: $"Unknown status code [{code}] — treated as terminal failure"),
    };
}
