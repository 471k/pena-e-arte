using FluentValidation;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Designs.Commands;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Designs.Validators;

/// <summary>Mirrors CreateAppointmentValidator's booking-content rules — RequestCatalogDesignCommand
/// carries its own CreateAppointmentRequest but is a distinct MediatR command type, so
/// CreateAppointmentValidator (registered against CreateAppointmentCommand specifically) does not
/// run for it automatically.</summary>
public class RequestCatalogDesignValidator : AbstractValidator<RequestCatalogDesignCommand>
{
    public RequestCatalogDesignValidator(IR2Service r2)
    {
        RuleFor(x => x.CatalogDesignId).NotEmpty();

        RuleFor(x => x.BookingRequest.Date)
            .GreaterThan(DateTime.UtcNow.AddMinutes(30))
            .WithMessage("Appointment must be at least 30 minutes in the future.");
        RuleFor(x => x.BookingRequest.DurationMinutes)
            .Must(d => BookingContentValidationRules.ValidDurations.Contains(d))
            .WithMessage($"Duration must be one of: {string.Join(", ", BookingContentValidationRules.ValidDurations)} minutes.");
        RuleFor(x => x.BookingRequest.Notes).MaximumLength(2000).When(x => x.BookingRequest.Notes is not null);
        RuleFor(x => x.BookingRequest.TattooDescription).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.BookingRequest.SafetyNotes).MaximumLength(2000).When(x => x.BookingRequest.SafetyNotes is not null);

        RuleForEach(x => x.BookingRequest.Images).ChildRules(image =>
        {
            image.RuleFor(i => i.Url).NotEmpty().MaximumLength(2048).Must(r2.IsR2Url)
                .WithMessage("Image Url must reference a valid storage URL.");
            image.RuleFor(i => i.Category).Must(c => BookingContentValidationRules.ValidImageCategories.Contains(c))
                .WithMessage("Category must be one of: AreaPhoto, Reference.");
        });
    }
}
