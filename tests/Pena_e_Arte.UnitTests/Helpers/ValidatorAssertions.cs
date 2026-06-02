using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;

namespace Pena_e_Arte.UnitTests.Helpers;

internal static class ValidatorAssertions
{
    internal static ValidationResult ShouldBeValid<T>(this AbstractValidator<T> validator, T instance)
    {
        ValidationResult result = validator.Validate(instance);
        result.IsValid.Should().BeTrue(
            because: $"validation should pass but got errors: {string.Join(", ", result.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}"))}");
        return result;
    }

    internal static ValidationResult ShouldFailOn<T>(this AbstractValidator<T> validator, T instance, string propertyName)
    {
        ValidationResult result = validator.Validate(instance);
        result.IsValid.Should().BeFalse(because: $"validation should fail on '{propertyName}'");
        result.Errors.Should().Contain(
            e => e.PropertyName == propertyName,
            because: $"expected a validation error for '{propertyName}' but got: [{string.Join(", ", result.Errors.Select(e => e.PropertyName))}]");
        return result;
    }
}
