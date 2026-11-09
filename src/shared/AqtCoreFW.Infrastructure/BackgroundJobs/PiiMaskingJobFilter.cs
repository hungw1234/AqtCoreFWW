using Hangfire.Client;
using Hangfire.Common;
using Hangfire.States;

namespace AqtCoreFW.Infrastructure.BackgroundJobs;

/// <summary>
/// Hangfire client filter — defense-in-depth PII guard for job arguments.
/// Coding standard: job args must contain IDs only, never raw PII values.
/// This filter logs a warning if a job method name suggests PII in args.
/// Full argument inspection is YAGNI — enforce via code review + ArchUnit.
/// </summary>
public sealed class PiiMaskingJobFilter : JobFilterAttribute, IClientFilter
{
    private static readonly HashSet<string> PiiFieldPatterns =
        ["email", "phone", "fullname", "cccd", "address", "password", "token"];

    public void OnCreating(CreatingContext context)
    {
        var jobName = context.Job.Method.Name.ToLowerInvariant();

        foreach (var pattern in PiiFieldPatterns)
        {
            if (jobName.Contains(pattern))
            {
                // Hangfire uses its own internal logger — surface via job properties
                context.SetJobParameter("pii_warning", $"Job name contains PII pattern '{pattern}'. Ensure args are IDs only.");
                break;
            }
        }
    }

    public void OnCreated(CreatedContext context) { }
}
