using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.Extensions.Logging;

namespace Pena_e_Arte.Infrastructure.Jobs;

/// <summary>
/// Global Hangfire state filter that emits one structured Serilog error line whenever ANY
/// background job (not just the handful with their own internal try/catch + LogWarning) lands
/// in the Failed state. Added 2026-09-05: before this, a job's unhandled exception was only
/// ever visible in the Hangfire dashboard itself — nothing reached Loki, so no log-based alert
/// rule could exist for "a Hangfire job failed" as a category. This is what
/// docs/infra/alerting-runbook.md's Hangfire failure-rate alert queries against.
/// </summary>
public class HangfireJobFailureLogFilter(ILogger<HangfireJobFailureLogFilter> logger) : IApplyStateFilter
{
    public void OnStateApplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
        if (context.NewState is not FailedState failedState) return;

        logger.LogError(failedState.Exception,
            "HangfireJobFailed {HangfireJobId} {HangfireJobType} {HangfireFailureReason}",
            context.BackgroundJob.Id,
            context.BackgroundJob.Job?.Type.FullName,
            failedState.Reason);
    }

    public void OnStateUnapplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
    }
}
