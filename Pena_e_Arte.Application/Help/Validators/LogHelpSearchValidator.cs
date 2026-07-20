using FluentValidation;
using Pena_e_Arte.Application.Help.Commands;

namespace Pena_e_Arte.Application.Help.Validators;

public class LogHelpSearchValidator : AbstractValidator<LogHelpSearchCommand>
{
    public LogHelpSearchValidator()
    {
        RuleFor(x => x.Request.Query)
            .NotEmpty()
            .MaximumLength(200)
            .WithMessage("Query is required and must be at most 200 characters.");

        RuleFor(x => x.Request.ResultCount)
            .GreaterThanOrEqualTo(0)
            .WithMessage("ResultCount must not be negative.");
    }
}
