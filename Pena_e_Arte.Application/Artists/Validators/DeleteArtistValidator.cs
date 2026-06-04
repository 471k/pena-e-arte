using FluentValidation;
using Pena_e_Arte.Application.Artists.Commands;

namespace Pena_e_Arte.Application.Artists.Validators;

public class DeleteArtistValidator : AbstractValidator<DeleteArtistCommand>
{
    public DeleteArtistValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
