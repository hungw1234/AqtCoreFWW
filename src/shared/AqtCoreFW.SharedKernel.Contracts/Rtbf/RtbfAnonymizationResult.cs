namespace AqtCoreFW.SharedKernel.Contracts.Rtbf;

/// <summary>
/// Result of one module's PII anonymization step in the RTBF pipeline.
/// Returned by each IModulePiiAnonymizer implementation.
/// </summary>
public sealed record RtbfAnonymizationResult(
    string ModuleName,
    bool Succeeded,
    int RecordsAnonymized,
    string? ErrorMessage = null)
{
    public static RtbfAnonymizationResult Ok(string module, int records) =>
        new(module, true, records);

    public static RtbfAnonymizationResult Fail(string module, string error) =>
        new(module, false, 0, error);
}
