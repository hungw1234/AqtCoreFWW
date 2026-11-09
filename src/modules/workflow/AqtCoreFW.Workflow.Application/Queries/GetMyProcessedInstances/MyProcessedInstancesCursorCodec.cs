using System.Text;

namespace AqtCoreFW.Workflow.Application.Queries.GetMyProcessedInstances;

/// <summary>
/// Encodes/decodes the opaque cursor token for "my processed instances" keyset pagination.
/// Format: base64(utf8("{instanceId}"))
/// Sort key is InstanceId DESC (internal bigint PK — monotonic with creation order).
/// TryDecode returns false for null, empty, malformed base64, or non-long value — never throws.
/// </summary>
internal static class MyProcessedInstancesCursorCodec
{
    /// <summary>Encodes a single InstanceId (long) into an opaque base64 cursor string.</summary>
    internal static string Encode(long instanceId)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(instanceId.ToString()));

    /// <summary>
    /// Attempts to decode a cursor string into the InstanceId (long).
    /// Returns false for null, empty, malformed base64, or non-parseable long — never throws.
    /// </summary>
    internal static bool TryDecode(string? cursor, out long instanceId)
    {
        instanceId = default;

        if (string.IsNullOrEmpty(cursor))
            return false;

        string raw;
        try
        {
            raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
        }
        catch (FormatException)
        {
            return false;
        }

        return long.TryParse(raw, out instanceId);
    }
}
