using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.SharedKernel.AI;

/// <summary>
/// Determines whether a data classification requires local (on-premises) AI processing.
/// Rule: Classification >= Confidential → local only (QĐ742 / GOV data sovereignty).
/// </summary>
public interface IDataSovereigntyChecker
{
    bool RequiresLocalProcessing(ClassificationLevel classification);
}
