using FluentValidation;
using Pena_e_Arte.Application.Feedback.Commands;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Feedback.Validators;

public class UpdateFeedbackStatusValidator : AbstractValidator<UpdateFeedbackStatusCommand>
{
    public UpdateFeedbackStatusValidator()
    {
        RuleFor(x => x.Request.Status)
            .NotEmpty()
            .Must(v => Enum.TryParse<FeedbackStatus>(v, ignoreCase: true, out _))
            .WithMessage("Status must be Open, Reviewing, Resolved, or Dismissed.");

        RuleFor(x => x.Request.AdminNote)
            .MaximumLength(1000)
            .When(x => x.Request.AdminNote is not null);
    }
}
