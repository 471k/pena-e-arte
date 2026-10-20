using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pena_e_Arte.Infrastructure.Jobs;
using Pena_e_Arte.Infrastructure.Services;
using StackExchange.Redis;

namespace Pena_e_Arte.UnitTests.Services;

/// <summary>C-5: a burst of POK webhooks (3 in ~3 s per payment) becomes one reconciliation run.</summary>
public class JobSchedulerReconciliationDebounceTests
{
    private readonly IBackgroundJobClient _jobs = Substitute.For<IBackgroundJobClient>();
    private readonly IDatabase _cache = Substitute.For<IDatabase>();

    private JobScheduler CreateSut()
    {
        IConnectionMultiplexer redis = Substitute.For<IConnectionMultiplexer>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(_cache);
        return new JobScheduler(_jobs, redis, NullLogger<JobScheduler>.Instance);
    }

    private void RedisWindowIsFree(bool free) =>
        _cache.StringSet(JobScheduler.ReconciliationDebounceKey, Arg.Any<RedisValue>(),
                (TimeSpan?)JobScheduler.ReconciliationDebounce, When.NotExists, Arg.Any<CommandFlags>())
            .Returns(free);

    [Fact]
    public void FirstTrigger_SchedulesOneReconciliationRunAtTheEndOfTheWindow()
    {
        RedisWindowIsFree(true);

        CreateSut().TriggerPaymentReconciliationNow();

        _jobs.Received(1).Create(
            Arg.Is<Job>(j => j.Type == typeof(PaymentReconciliationJob) && j.Method.Name == nameof(PaymentReconciliationJob.RunAsync)),
            Arg.Is<IState>(s => s is ScheduledState));
        _jobs.DidNotReceive().Create(Arg.Any<Job>(), Arg.Is<IState>(s => s is EnqueuedState));
    }

    [Fact]
    public void TriggerInsideTheWindow_IsSkipped_BecauseTheScheduledRunStartsAfterIt()
    {
        RedisWindowIsFree(false);

        CreateSut().TriggerPaymentReconciliationNow();

        _jobs.DidNotReceive().Create(Arg.Any<Job>(), Arg.Any<IState>());
    }

    [Fact]
    public void RedisUnavailable_RunsAtOnce_AsBefore()
    {
        _cache.StringSet(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), Arg.Any<When>(), Arg.Any<CommandFlags>())
            .Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

        CreateSut().TriggerPaymentReconciliationNow();

        _jobs.Received(1).Create(
            Arg.Is<Job>(j => j.Type == typeof(PaymentReconciliationJob)),
            Arg.Is<IState>(s => s is EnqueuedState));
    }

    [Fact]
    public void TheWindowIsShortEnoughToKeepWebhooksUseful()
    {
        JobScheduler.ReconciliationDebounce.Should().BeGreaterThan(TimeSpan.FromSeconds(3)) // covers POK's 3 tries
            .And.BeLessThanOrEqualTo(TimeSpan.FromSeconds(30));
    }
}
