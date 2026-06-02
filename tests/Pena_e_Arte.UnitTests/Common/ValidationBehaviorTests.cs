using FluentAssertions;
using FluentValidation;
using MediatR;
using Pena_e_Arte.Application.Common.Behaviors;

namespace Pena_e_Arte.UnitTests.Common;

public record FakeCommand : IRequest<string>;

file sealed class PassingValidator : AbstractValidator<FakeCommand>
{
    public PassingValidator() => RuleFor(x => x).NotNull();
}

file sealed class FailingValidator : AbstractValidator<FakeCommand>
{
    public FailingValidator(string field = "Field", string message = "Error")
        => RuleFor(x => x).Must(_ => false).WithName(field).WithMessage(message);
}

public class ValidationBehaviorTests
{
    [Fact]
    public async Task Handle_NoValidators_CallsNextAndReturnsResult()
    {
        ValidationBehavior<FakeCommand, string> behavior = new([]);

        string result = await behavior.Handle(
            new FakeCommand(),
            _ => Task.FromResult("ok"),
            CancellationToken.None);

        result.Should().Be("ok");
    }

    [Fact]
    public async Task Handle_PassingValidator_CallsNextAndReturnsResult()
    {
        ValidationBehavior<FakeCommand, string> behavior = new([new PassingValidator()]);

        string result = await behavior.Handle(
            new FakeCommand(),
            _ => Task.FromResult("ok"),
            CancellationToken.None);

        result.Should().Be("ok");
    }

    [Fact]
    public async Task Handle_FailingValidator_ThrowsValidationException()
    {
        ValidationBehavior<FakeCommand, string> behavior = new([new FailingValidator()]);

        Func<Task> act = () => behavior.Handle(
            new FakeCommand(),
            _ => Task.FromResult("ok"),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Handle_FailingValidator_DoesNotCallNext()
    {
        ValidationBehavior<FakeCommand, string> behavior = new([new FailingValidator()]);
        bool nextCalled = false;

        try
        {
            await behavior.Handle(
                new FakeCommand(),
                _ => { nextCalled = true; return Task.FromResult("ok"); },
                CancellationToken.None);
        }
        catch (ValidationException) { }

        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_MultipleFailures_AggregatesAllErrors()
    {
        ValidationBehavior<FakeCommand, string> behavior = new([
            new FailingValidator("Field1", "Error 1"),
            new FailingValidator("Field2", "Error 2")
        ]);

        ValidationException ex = await Assert.ThrowsAsync<ValidationException>(
            () => behavior.Handle(new FakeCommand(), _ => Task.FromResult("ok"), CancellationToken.None));

        ex.Errors.Should().HaveCount(2);
        ex.Errors.Should().Contain(e => e.ErrorMessage == "Error 1");
        ex.Errors.Should().Contain(e => e.ErrorMessage == "Error 2");
    }

    [Fact]
    public async Task Handle_MixedPassingAndFailingValidators_ThrowsForFailures()
    {
        ValidationBehavior<FakeCommand, string> behavior = new([
            new PassingValidator(),
            new FailingValidator()
        ]);

        Func<Task> act = () => behavior.Handle(
            new FakeCommand(),
            _ => Task.FromResult("ok"),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }
}
