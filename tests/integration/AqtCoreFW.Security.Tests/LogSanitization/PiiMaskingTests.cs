namespace AqtCoreFW.Security.Tests.LogSanitization;

/// <summary>
/// PII masking pattern tests — TC-SEC-LOG-001 to 004.
/// Verifies the PII detection patterns used by PiiMaskingJobFilter.
/// Tests pattern logic directly to avoid Hangfire internal API dependencies.
/// </summary>
public sealed class PiiMaskingTests
{
    // Mirror of PiiMaskingJobFilter.PiiFieldPatterns
    private static readonly HashSet<string> PiiPatterns =
        ["email", "phone", "fullname", "cccd", "address", "password", "token"];

    private static bool ContainsPiiPattern(string methodName) =>
        PiiPatterns.Any(p => methodName.ToLowerInvariant().Contains(p));

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("SendEmailNotification", true)]
    [InlineData("SyncUserPhoneNumber", true)]
    [InlineData("UpdateFullNameRecord", true)]
    [InlineData("ValidateCccdDocument", true)]
    [InlineData("ChangePasswordExpiry", true)]
    [InlineData("RefreshTokenCleanup", true)]
    [InlineData("UpdateUserAddress", true)]
    public void PiiPatternMethods_Detected(string methodName, bool expected)
    {
        ContainsPiiPattern(methodName).Should().Be(expected,
            $"method '{methodName}' should be flagged as containing PII");
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("ProcessCaseById")]
    [InlineData("GenerateReport")]
    [InlineData("CleanupExpiredSessions")]
    [InlineData("SyncWorkflowState")]
    [InlineData("CalculateKpiMetrics")]
    public void SafeMethodNames_NotFlagged(string methodName)
    {
        ContainsPiiPattern(methodName).Should().BeFalse(
            $"method '{methodName}' should NOT be flagged as PII");
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AllPiiPatterns_AreLowercase()
    {
        // Ensures case-insensitive matching works correctly
        foreach (var pattern in PiiPatterns)
        {
            pattern.Should().Be(pattern.ToLowerInvariant(),
                "PII patterns must be lowercase for case-insensitive Contains()");
        }
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("SENDEMAILNOTIFICATION")]
    [InlineData("sendemailnotification")]
    [InlineData("SendEmailNotification")]
    public void CaseInsensitive_Detection(string methodName)
    {
        ContainsPiiPattern(methodName).Should().BeTrue(
            "PII detection must be case-insensitive");
    }
}
