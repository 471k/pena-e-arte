using FluentValidation;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Reminders.Commands;
using Pena_e_Arte.Contracts.Requests;

namespace Pena_e_Arte.Application.Reminders.Validators;

public class CreateManualReminderValidator : AbstractValidator<CreateManualReminderCommand>
{
    public CreateManualReminderValidator()
    {
        RuleFor(x => x.Request).Must(HaveExactlyOneRecipientSource)
            .WithMessage("Provide exactly one of: appointmentId, clientId, or a name and phone.");

        RuleFor(x => x.Request.RecipientName)
            .NotEmpty().MaximumLength(200)
            .When(x => x.Request.AppointmentId is null && x.Request.ClientId is null);

        RuleFor(x => x.Request.RecipientPhone)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().MaximumLength(20)
            .Matches(PhoneValidationRules.E164Format)
            .WithMessage(PhoneValidationRules.E164ErrorMessage)
            .When(x => x.Request.AppointmentId is null && x.Request.ClientId is null);

        RuleFor(x => x.Request.Message).MaximumLength(320);

        RuleFor(x => x.Request.ScheduledFor)
            .GreaterThan(DateTime.UtcNow)
            .LessThanOrEqualTo(DateTime.UtcNow.AddDays(90))
            .When(x => x.Request.ScheduledFor.HasValue);
    }

    private static bool HaveExactlyOneRecipientSource(CreateManualReminderRequest r)
    {
        int sources = 0;
        if (r.AppointmentId is not null) sources++;
        if (r.ClientId is not null) sources++;
        if (!string.IsNullOrWhiteSpace(r.RecipientName) && !string.IsNullOrWhiteSpace(r.RecipientPhone)) sources++;
        return sources == 1;
    }
}
