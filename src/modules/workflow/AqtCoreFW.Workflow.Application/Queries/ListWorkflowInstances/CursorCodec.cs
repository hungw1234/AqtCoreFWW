using System.Globalization;
using System.Text;

namespace AqtCoreFW.Workflow.Application.Queries.ListWorkflowInstances;

/// <summary>
/// Encodes/decodes the opaque cursor token for keyset pagination.
/// Format: base64(utf8("{startedAt:O}|{internalId}"))
/// StartedAt uses 'O' (round-trip) DateTimeOffset format to preserve UTC offset exactly.
/// internalId: long (WorkflowInstance.Id bigint PK) — SQL-translatable for `i.Id &lt; lastId`.
///   Note: not the PublicId Guid — long comparison is required for SQL keyset predicate [RT-01].
/// TryDecode: bool-return, never throws — all parse ops guarded. [RT-03]
/// Phase-4b B4c: changed Id type from Guid to long (WorkflowInstance.Id internal bigint PK).
/// </summary>
internal static class CursorCodec
{
    /// <summary>Encodes a (StartedAt, internalId) pair into an opaque base64 cursor string.</summary>
    internal static string Encode(DateTimeOffset startedAt, long internalId)
    {
        var raw = $"{startedAt:O}|{internalId}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
    }

    /// <summary>
    /// Attempts to decode a cursor string into its (StartedAt, InternalId) components.
    /// Returns false for null, empty, malformed base64, bad date, or bad long — never throws. [RT-03]
    /// </summary>
    internal static bool TryDecode(string? cursor, out (DateTimeOffset StartedAt, long Id) result)
    {
        result = default;

        if (string.IsNullOrEmpty(cursor))
            return false;

        string raw;
        try
        {
            // Guarded: malformed base64 throws FormatException [RT-03]
            raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
        }
        catch (FormatException)
        {
            return false;
        }

        var parts = raw.Split('|', 2);
        if (parts.Length != 2)
            return false;

        // DateTimeOffset.TryParse with RoundtripKind preserves UTC offset [RT-03]
        if (!DateTimeOffset.TryParse(
                parts[0],
                null,
                DateTimeStyles.RoundtripKind,
                out var startedAt))
            return false;

        if (!long.TryParse(parts[1], out var id))
            return false;

        result = (startedAt, id);
        return true;
    }
}
