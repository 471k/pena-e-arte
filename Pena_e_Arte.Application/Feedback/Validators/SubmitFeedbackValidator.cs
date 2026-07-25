using FluentValidation;
using Pena_e_Arte.Application.Feedback.Commands;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Feedback.Validators;

public class SubmitFeedbackValidator : AbstractValidator<SubmitFeedbackCommand>
{
    private const int MaxAttachments = 3;

    public SubmitFeedbackValidator(ICurrentUser currentUser, ICurrentTenant currentTenant, IR2Service r2)
    {
        // A studio-less client (registered with no studio, or between studios) has no
        // tenant_id claim, so ICurrentTenant is never set for their request — reaching this
        // far and having SubmitFeedbackHandler look up a nonexistent Studio would otherwise
        // throw an unhandled 500 instead of a clean, actionable message.
        RuleFor(x => x)
            .Must(_ => currentTenant.IsSet)
            .WithName("Studio")
            .WithMessage("You need to belong to a studio to submit feedback.");

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

        RuleFor(x => x.Request.AttachmentUrls)
            .Must(urls => urls == null || urls.Count <= MaxAttachments)
            .WithMessage($"You can attach up to {MaxAttachments} files.");
        RuleForEach(x => x.Request.AttachmentUrls)
            .NotEmpty()
            .MaximumLength(2048)
            .Must(r2.IsR2Url)
            .WithMessage("AttachmentUrls must reference a valid storage URL.")
            .When(x => x.Request.AttachmentUrls is not null);
    }
}
