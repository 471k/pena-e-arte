using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Pena_e_Arte.Infrastructure.Jobs;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Jobs;

public class HangfireJobOutcomeLogFilterTests
{
    private readonly ListLogger<HangfireJobOutcomeLogFilter> _logger = new();
    private readonly IStorageConnection _connection = Substitute.For<IStorageConnection>();
    private readonly IWriteOnlyTransaction _transaction = Substitute.For<IWriteOnlyTransaction>();

    private ApplyStateContext Context(IState newState) => new(
        Substitute.For<JobStorage>(),
        _connection,
        _transaction,
        new BackgroundJob("42", Job.FromExpression<TrialExpiryJob>(j => j.ExecuteAsync(Guid.Empty, default)), DateTime.UtcNow),
        newState,
        ProcessingState.StateName);

    [Fact]
    public void Succeeded_LogsOneHeartbeatLine_WithTypeMethodAndDuration()
    {
        new HangfireJobOutcomeLogFilter(_logger).OnStateApplied(
            Context(new SucceededState(null, latency: 5, performanceDuration: 1234)), _transaction);

        _logger.Messages(LogLevel.Information).Should().ContainSingle()
            .Which.Should().Be("HangfireJobSucceeded 42 TrialExpiryJob.ExecuteAsync 1234");
    }

    [Fact]
    public void Failed_StillLogsTheLineTheAlertMatches()
    {
        new HangfireJobOutcomeLogFilter(_logger).OnStateApplied(
            Context(new FailedState(new InvalidOperationException("boom"))), _transaction);

        _logger.Messages(LogLevel.Error).Should().ContainSingle()
            .Which.Should().StartWith("HangfireJobFailed 42 Pena_e_Arte.Infrastructure.Jobs.TrialExpiryJob");
    }

    [Fact]
    public void OtherStates_LogNothing()
    {
        new HangfireJobOutcomeLogFilter(_logger).OnStateApplied(
            Context(new EnqueuedState()), _transaction);

        _logger.Entries.Should().BeEmpty();
    }

    // Desktop's condition (17 Nov): nothing in the state-transition path may cost a storage round-trip.
    // Everything the filter logs must come from the in-memory context.
    [Fact]
    public void NeverTouchesStorage()
    {
        HangfireJobOutcomeLogFilter filter = new(_logger);

        filter.OnStateApplied(Context(new SucceededState(null, 1, 1)), _transaction);
        filter.OnStateApplied(Context(new FailedState(new InvalidOperationException("boom"))), _transaction);

        _connection.ReceivedCalls().Should().BeEmpty();
        _transaction.ReceivedCalls().Should().BeEmpty();
    }
}
