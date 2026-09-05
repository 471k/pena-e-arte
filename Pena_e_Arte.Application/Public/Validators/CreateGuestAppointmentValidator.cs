using FluentValidation;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Public.Commands;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Public.Validators;

public class CreateGuestAppointmentValidator : AbstractValidator<CreateGuestAppointmentCommand>
{
    // Booking-content rules below deliberately mirror CreateAppointmentValidator's — this
    // codebase has no established nested-command-validator composition convention (verified: no
    // existing SetValidator usage), and the two commands wrap different outer types (a raw
    // CreateAppointmentRequest here vs. a CreateAppointmentCommand there), so duplicating the
    // rule set against `x.Request.Booking.*` paths is the smallest change that stays consistent
    // with this codebase's existing "one self-contained validator per command" style. The
    // underlying constants and the phone regex ARE shared (BookingContentValidationRules,
    // PhoneValidationRules) — only the FluentValidation call sites are duplicated, not the
    // rules themselves. Found via /code-review, 2026-09-01.
    public CreateGuestAppointmentValidator(IR2Service r2)
    {
        RuleFor(x => x.StudioSlug).NotEmpty();

        RuleFor(x => x.Request.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Request.Phone)
            .NotEmpty()
            .MaximumLength(20)
            .Matches(PhoneValidationRules.E164Format)
            .WithMessage(PhoneValidationRules.E164ErrorMessage);

        RuleFor(x => x.Request.Booking.Date)
            .GreaterThan(DateTime.UtcNow.AddMinutes(30))
            .WithMessage("Appointment must be at least 30 minutes in the future.");
        RuleFor(x => x.Request.Booking.DurationMinutes)
            .Must(d => BookingContentValidationRules.ValidDurations.Contains(d))
            .WithMessage($"Duration must be one of: {string.Join(", ", BookingContentValidationRules.ValidDurations)} minutes.");
        RuleFor(x => x.Request.Booking.Notes).MaximumLength(2000).When(x => x.Request.Booking.Notes is not null);

        RuleFor(x => x.Request.Booking.TattooDescription).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.Request.Booking.SafetyNotes)
            .MaximumLength(2000)
            .When(x => x.Request.Booking.SafetyNotes is not null);
        RuleForEach(x => x.Request.Booking.DesiredPlacementLocations).NotEmpty().MaximumLength(200);

        RuleFor(x => x.Request.Booking.ReferralSource)
            .Must(s => s is null || BookingContentValidationRules.ValidReferralSources.Contains(s))
            .WithMessage("ReferralSource must be one of: " + string.Join(", ", BookingContentValidationRules.ValidReferralSources));
        RuleFor(x => x.Request.Booking.ReferralSourceOther)
            .NotEmpty()
            .WithMessage("Please tell us how you heard about us.")
            .When(x => x.Request.Booking.ReferralSource == "Other");

        // Both categories required for the guest flow (Decision #6 — the authenticated form
        // leaves both optional, see Part 6d note).
        RuleFor(x => x.Request.Booking.Images)
            .Must(images => images is not null && images.Any(i => i.Category == "AreaPhoto"))
            .WithMessage("Please attach a photo of the area to be tattooed.");
        RuleFor(x => x.Request.Booking.Images)
            .Must(images => images is not null && images.Any(i => i.Category == "Reference"))
            .WithMessage("Please attach at least one reference image.");

        RuleForEach(x => x.Request.Booking.Images).ChildRules(image =>
        {
            image.RuleFor(i => i.Url).NotEmpty().MaximumLength(2048).Must(r2.IsR2Url)
                .WithMessage("Image Url must reference a valid storage URL.");
            image.RuleFor(i => i.Category).Must(c => BookingContentValidationRules.ValidImageCategories.Contains(c))
                .WithMessage("Category must be one of: AreaPhoto, Reference.");
        });

        RuleFor(x => x.Request.Booking.Images)
            .Must(images => images is null ||
                images.Where(i => i.Category == "AreaPhoto").Count() <= BookingContentValidationRules.MaxImagesPerCategory)
            .WithMessage($"You can attach up to {BookingContentValidationRules.MaxImagesPerCategory} area photos.");
        RuleFor(x => x.Request.Booking.Images)
            .Must(images => images is null ||
                images.Where(i => i.Category == "Reference").Count() <= BookingContentValidationRules.MaxImagesPerCategory)
            .WithMessage($"You can attach up to {BookingContentValidationRules.MaxImagesPerCategory} reference images.");
    }
}
