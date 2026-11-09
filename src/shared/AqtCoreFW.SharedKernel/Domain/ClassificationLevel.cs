namespace AqtCoreFW.SharedKernel.Domain;

/// <summary>Data classification per QĐ742 cấp 4 security requirements.</summary>
public enum ClassificationLevel
{
    Public = 0,
    Internal = 1,
    Confidential = 2,
    Secret = 3,
    TopSecret = 4
}
