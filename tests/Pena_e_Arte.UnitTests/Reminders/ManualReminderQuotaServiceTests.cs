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

    private void MockScriptResult(long count)
    {
        _db.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>(), Arg.Any<CommandFlags>())
           .Returns(Task.FromResult(RedisResult.Create(count)));
    }

    [Fact]
    public async Task CheckAndIncrementAsync_UnderLimit_DoesNotThrow()
    {
        MockScriptResult(5);

        Func<Task> act = () => CreateSut().CheckAndIncrementAsync(Guid.NewGuid(), Guid.NewGuid(), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CheckAndIncrementAsync_AtLimit_ThrowsManualReminderQuotaExceededException()
    {
        MockScriptResult(21);

        Func<Task> act = () => CreateSut().CheckAndIncrementAsync(Guid.NewGuid(), Guid.NewGuid(), default);

        await act.Should().ThrowAsync<ManualReminderQuotaExceededException>();
    }

    [Fact]
    public async Task CheckAndIncrementAsync_AtExactlyTwenty_DoesNotThrow()
    {
        MockScriptResult(20);

        Func<Task> act = () => CreateSut().CheckAndIncrementAsync(Guid.NewGuid(), Guid.NewGuid(), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CheckAndIncrementAsync_EvaluatesTheAtomicIncrExpireScript()
    {
        MockScriptResult(1);

        await CreateSut().CheckAndIncrementAsync(Guid.NewGuid(), Guid.NewGuid(), default);

        await _db.Received(1).ScriptEvaluateAsync(
            Arg.Is<string>(s => s.Contains("INCR") && s.Contains("EXPIRE")),
            Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>(), Arg.Any<CommandFlags>());
    }
}
