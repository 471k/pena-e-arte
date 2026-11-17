using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.Extensions.Logging;

namespace Pena_e_Arte.Infrastructure.Jobs;

/// <summary>
/// Global Hangfire state filter: one structured log line whenever ANY background job finishes, either way.
///
/// Failed (since 2026-09-05): one Error line, "HangfireJobFailed". Before this, a job's unhandled
/// exception showed only in the Hangfire dashboard and never reached Loki. The Hangfire failure-rate
/// alert in docs/infra/alerting-runbook.md queries this line, so its text must not change.
///
/// Succeeded (since 2026-11-17): one Information line, "HangfireJobSucceeded", for EVERY job, recurring
/// or scheduled per entity. "Did it run?" is now one Loki query for any job. Every job also writes its own
/// outcome line (architecture.md "Jobs: one line per run"). This line covers the job that forgot to.
///
/// It logs every job rather than only recurring ones: telling them apart needs the RecurringJobId job
/// parameter, and Hangfire.MySqlStorage 2.0.3 never fills BackgroundJob.ParametersSnapshot (the
/// assembly has no reference to it). So reading the parameter would mean a storage round-trip inside
/// the state transition, on every job. Everything logged here comes from the in-memory context and state.
/// </summary>
public class HangfireJobOutcomeLogFilter(ILogger<HangfireJobOutcomeLogFilter> logger) : IApplyStateFilter
{
    public void OnStateApplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
        switch (context.NewState)
        {
            case FailedState failedState:
                logger.LogError(failedState.Exception,
                    "HangfireJobFailed {HangfireJobId} {HangfireJobType} {HangfireFailureReason}",
                    context.BackgroundJob.Id,
                    context.BackgroundJob.Job?.Type.FullName,
                    failedState.Reason);
                break;

            case SucceededState succeededState:
                // Type + method, because ExchangeRateFetchJob registers two methods (fetch and stale check).
                logger.LogInformation(
                    "HangfireJobSucceeded {HangfireJobId} {HangfireJobType}.{HangfireJobMethod} {DurationMs}",
                    context.BackgroundJob.Id,
                    context.BackgroundJob.Job?.Type.Name,
                    context.BackgroundJob.Job?.Method.Name,
                    succeededState.PerformanceDuration);
                break;
        }
    }

    public void OnStateUnapplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
    }
}
