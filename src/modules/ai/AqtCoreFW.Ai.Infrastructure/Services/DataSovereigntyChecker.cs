using AqtCoreFW.SharedKernel.AI;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Ai.Infrastructure.Services;

/// <summary>
/// Determines whether data requires local (on-premises) processing.
/// Rule: Classification >= Confidential → local only (QĐ742 / GOV sovereignty).
/// Singleton — no state, pure function.
/// </summary>
public sealed class DataSovereigntyChecker : IDataSovereigntyChecker
{
    public bool RequiresLocalProcessing(ClassificationLevel classification)
        => classification >= ClassificationLevel.Confidential;
}
