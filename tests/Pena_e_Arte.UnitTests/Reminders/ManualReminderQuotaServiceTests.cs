using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Infrastructure.Services;
using StackExchange.Redis;

namespace Pena_e_Arte.UnitTests.Reminders;

public class ManualReminderQuotaServiceTests
{
    private readonly IConnectionMultiplexer _redis = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _db = Substitute.For<IDatabase>();

    public ManualReminderQuotaServiceTests()
    {
        _redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).ReturnsForAnyArgs(_db);
    }

    private ManualReminderQuotaService CreateSut() => new(_redis);

    [Fact]
    public async Task CheckAndIncrementAsync_UnderLimit_DoesNotThrow()
    {
        _db.StringIncrementAsync(Arg.Any<RedisKey>(), Arg.Any<long>(), Arg.Any<CommandFlags>())
           .Returns(Task.FromResult(5L));

        Func<Task> act = () => CreateSut().CheckAndIncrementAsync(Guid.NewGuid(), Guid.NewGuid(), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CheckAndIncrementAsync_AtLimit_ThrowsManualReminderQuotaExceededException()
    {
        _db.StringIncrementAsync(Arg.Any<RedisKey>(), Arg.Any<long>(), Arg.Any<CommandFlags>())
           .Returns(Task.FromResult(21L));

        Func<Task> act = () => CreateSut().CheckAndIncrementAsync(Guid.NewGuid(), Guid.NewGuid(), default);

        await act.Should().ThrowAsync<ManualReminderQuotaExceededException>();
    }

    [Fact]
    public async Task CheckAndIncrementAsync_AtExactlyTwenty_DoesNotThrow()
    {
        _db.StringIncrementAsync(Arg.Any<RedisKey>(), Arg.Any<long>(), Arg.Any<CommandFlags>())
           .Returns(Task.FromResult(20L));

        Func<Task> act = () => CreateSut().CheckAndIncrementAsync(Guid.NewGuid(), Guid.NewGuid(), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CheckAndIncrementAsync_FirstCallOfDay_SetsExpiry()
    {
        _db.StringIncrementAsync(Arg.Any<RedisKey>(), Arg.Any<long>(), Arg.Any<CommandFlags>())
           .Returns(Task.FromResult(1L));

        await CreateSut().CheckAndIncrementAsync(Guid.NewGuid(), Guid.NewGuid(), default);

        await _db.Received(1).KeyExpireAsync(Arg.Any<RedisKey>(), TimeSpan.FromHours(25), Arg.Any<ExpireWhen>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task CheckAndIncrementAsync_SubsequentCallOfDay_DoesNotResetExpiry()
    {
        _db.StringIncrementAsync(Arg.Any<RedisKey>(), Arg.Any<long>(), Arg.Any<CommandFlags>())
           .Returns(Task.FromResult(2L));

        await CreateSut().CheckAndIncrementAsync(Guid.NewGuid(), Guid.NewGuid(), default);

        await _db.DidNotReceive().KeyExpireAsync(Arg.Any<RedisKey>(), Arg.Any<TimeSpan?>(), Arg.Any<ExpireWhen>(), Arg.Any<CommandFlags>());
    }
}
