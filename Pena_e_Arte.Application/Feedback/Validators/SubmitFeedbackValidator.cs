using FluentValidation;
using Pena_e_Arte.Application.Feedback.Commands;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Feedback.Validators;

public class SubmitFeedbackValidator : AbstractValidator<SubmitFeedbackCommand>
{
    public SubmitFeedbackValidator()
    {
        RuleFor(x => x.Request.Type)
            .NotEmpty()
            .Must(v => Enum.TryParse<FeedbackType>(v, ignoreCase: true, out _))
            .WithMessage("Type must be BugReport, FeatureRequest, or General.");

        RuleFor(x => x.Request.Title)
            .NotEmpty()
            .MaximumLength(150)
            .WithMessage("Title is required and must be at most 150 characters.");

        RuleFor(x => x.Request.Body)
            .NotEmpty()
            .MinimumLength(10)
            .MaximumLength(2000)
            .WithMessage("Description must be between 10 and 2000 characters.");
    }
}
