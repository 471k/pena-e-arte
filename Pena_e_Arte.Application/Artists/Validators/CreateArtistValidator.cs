using FluentValidation;
using Pena_e_Arte.Application.Artists.Commands;
using Pena_e_Arte.Application.Common;

namespace Pena_e_Arte.Application.Artists.Validators;

public class CreateArtistValidator : AbstractValidator<CreateArtistCommand>
{
    public CreateArtistValidator()
    {
        RuleFor(x => x.Request.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Request.HourlyRate).InclusiveBetween(0.01m, 10_000m)
            .When(x => x.Request.HourlyRate is not null);
        RuleFor(x => x.Request.Specializations).MustBeValidTattooStyles();
    }
}
