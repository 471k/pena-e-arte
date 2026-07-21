using FluentValidation;
using Pena_e_Arte.Application.Feedback.Commands;

namespace Pena_e_Arte.Application.Feedback.Validators;

public class PostFeedbackMessageValidator : AbstractValidator<PostFeedbackMessageCommand>
{
    public PostFeedbackMessageValidator()
    {
        RuleFor(x => x.Request.Body)
            .NotEmpty()
            .MaximumLength(2000)
            .WithMessage("Message is required and must be at most 2000 characters.");
    }
}
