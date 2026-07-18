using FluentAssertions;
using MediatR;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pena_e_Arte.Application.Common.Behaviors;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.UnitTests.Common;

public record PlainFakeCommand : IRequest<string>;

public record QuotaCheckedFakeCommand : IRequest<string>, IQuotaCheckedCommand
{
    public QuotaType QuotaType => QuotaType.Artists;
}

public class PlanLimitBehaviorTests
{
    private readonly IPlanLimitService _planLimits = Substitute.For<IPlanLimitService>();

    private PlanLimitBehavior<TRequest, string> CreateSut<TRequest>() where TRequest : notnull =>
        new(_planLimits);

    [Fact]
    public async Task Handle_PlainCommand_DoesNotCallPlanLimitService()
    {
        PlanLimitBehavior<PlainFakeCommand, string> behavior = CreateSut<PlainFakeCommand>();

        string result = await behavior.Handle(
            new PlainFakeCommand(),
            _ => Task.FromResult("ok"),
            CancellationToken.None);

        result.Should().Be("ok");
        await _planLimits.DidNotReceiveWithAnyArgs().EnsureWithinLimitAsync(default, default);
    }

    [Fact]
    public async Task Handle_QuotaCheckedCommand_CallsPlanLimitServiceWithCorrectQuotaType()
    {
        PlanLimitBehavior<QuotaCheckedFakeCommand, string> behavior = CreateSut<QuotaCheckedFakeCommand>();

        await behavior.Handle(
            new QuotaCheckedFakeCommand(),
            _ => Task.FromResult("ok"),
            CancellationToken.None);

        await _planLimits.Received(1).EnsureWithinLimitAsync(QuotaType.Artists, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_QuotaCheckedCommand_UnderLimit_CallsNextAndReturnsResult()
    {
        PlanLimitBehavior<QuotaCheckedFakeCommand, string> behavior = CreateSut<QuotaCheckedFakeCommand>();

        string result = await behavior.Handle(
            new QuotaCheckedFakeCommand(),
            _ => Task.FromResult("ok"),
            CancellationToken.None);

        result.Should().Be("ok");
    }

    [Fact]
    public async Task Handle_QuotaCheckedCommand_OverLimit_ThrowsAndDoesNotCallNext()
    {
        _planLimits.EnsureWithinLimitAsync(Arg.Any<QuotaType>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new PlanLimitExceededException("limit reached"));

        PlanLimitBehavior<QuotaCheckedFakeCommand, string> behavior = CreateSut<QuotaCheckedFakeCommand>();
        bool nextCalled = false;

        Func<Task> act = () => behavior.Handle(
            new QuotaCheckedFakeCommand(),
            _ => { nextCalled = true; return Task.FromResult("ok"); },
            CancellationToken.None);

        await act.Should().ThrowAsync<PlanLimitExceededException>();
        nextCalled.Should().BeFalse();
    }
}
