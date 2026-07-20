using FluentValidation;
using Pena_e_Arte.Application.Feedback.Commands;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Feedback.Validators;

public class SubmitFeedbackValidator : AbstractValidator<SubmitFeedbackCommand>
{
    public SubmitFeedbackValidator(ICurrentUser currentUser)
    {
        RuleFor(x => x.Request.Type)
            .NotEmpty()
            .Must(v => Enum.TryParse<FeedbackType>(v, ignoreCase: true, out _))
            .WithMessage("Type must be BugReport, FeatureRequest, General, or SupportRequest.");

        // Clients can only reach this endpoint via the Help menu's Contact Support flow —
        // Bug Report / Feature Request / General stay restricted to studio staff.
        RuleFor(x => x.Request.Type)
            .Must(v => !string.Equals(currentUser.Role, "client", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(v, "SupportRequest", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Clients can only submit support requests.");

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
