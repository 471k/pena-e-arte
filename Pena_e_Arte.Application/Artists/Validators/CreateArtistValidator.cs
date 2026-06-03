using FluentValidation;
using Pena_e_Arte.Application.Artists.Commands;

namespace Pena_e_Arte.Application.Artists.Validators;

public class CreateArtistValidator : AbstractValidator<CreateArtistCommand>
{
    public CreateArtistValidator()
    {
        RuleFor(x => x.Request.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Request.Specializations).MaximumLength(1000)
            .When(x => x.Request.Specializations is not null);
    }
}
