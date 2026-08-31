using FluentValidation;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Appointments.Validators;

public class CreateAppointmentValidator : AbstractValidator<CreateAppointmentCommand>
{
    // Mirrors BookAppointmentForm.tsx's VALID_DURATIONS — the session-length options
    // actually offered in the booking form.
    private static readonly int[] ValidDurations = [30, 45, 60, 90, 120, 180, 240, 300, 360, 480];

    // Mirrors CategorizedImagesField's per-category cap (Part 6).
    private const int MaxImagesPerCategory = 6;

    private static readonly HashSet<string> ValidImageCategories = ["AreaPhoto", "Reference"];
    private static readonly HashSet<string> ValidReferralSources =
        ["Instagram", "TikTok", "YouTube", "FriendsAndFamily", "Other"];

    public CreateAppointmentValidator(IR2Service r2)
    {
        RuleFor(x => x.Request.ClientId).NotEmpty();
        RuleFor(x => x.Request.Date)
            .GreaterThan(DateTime.UtcNow.AddMinutes(30))
            .WithMessage("Appointment must be at least 30 minutes in the future.");
        RuleFor(x => x.Request.DurationMinutes)
            .Must(d => ValidDurations.Contains(d))
            .WithMessage($"Duration must be one of: {string.Join(", ", ValidDurations)} minutes.");
        RuleFor(x => x.Request.Notes).MaximumLength(2000).When(x => x.Request.Notes is not null);

        RuleFor(x => x.Request.TattooDescription).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.Request.SafetyNotes).MaximumLength(2000).When(x => x.Request.SafetyNotes is not null);

        // Zone ids are validated leniently (non-empty, bounded length) — mirrors
        // UpdateBodyMapValidator's existing precedent for ClientProfile.BodyMap.Locations,
        // which likewise does not check against a closed server-side zone-id set.
        RuleForEach(x => x.Request.DesiredPlacementLocations).NotEmpty().MaximumLength(200);

        RuleFor(x => x.Request.ReferralSource)
            .Must(s => s is null || ValidReferralSources.Contains(s))
            .WithMessage("ReferralSource must be one of: " + string.Join(", ", ValidReferralSources));
        RuleFor(x => x.Request.ReferralSourceOther)
            .NotEmpty()
            .WithMessage("Please tell us how you heard about us.")
            .When(x => x.Request.ReferralSource == "Other");

        RuleForEach(x => x.Request.Images).ChildRules(image =>
        {
            image.RuleFor(i => i.Url).NotEmpty().MaximumLength(2048).Must(r2.IsR2Url)
                .WithMessage("Image Url must reference a valid storage URL.");
            image.RuleFor(i => i.Category).Must(c => ValidImageCategories.Contains(c))
                .WithMessage("Category must be one of: AreaPhoto, Reference.");
        });

        // Each category capped independently — a guest/client shouldn't be able to put
        // MaxImagesPerCategory * 2 images all in one category.
        RuleFor(x => x.Request.Images)
            .Must(images => images is null ||
                images.Where(i => i.Category == "AreaPhoto").Count() <= MaxImagesPerCategory)
            .WithMessage($"You can attach up to {MaxImagesPerCategory} area photos.");
        RuleFor(x => x.Request.Images)
            .Must(images => images is null ||
                images.Where(i => i.Category == "Reference").Count() <= MaxImagesPerCategory)
            .WithMessage($"You can attach up to {MaxImagesPerCategory} reference images.");
    }
}
