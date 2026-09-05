using FluentValidation;
using Pena_e_Arte.Application.Clients.Commands;

namespace Pena_e_Arte.Application.Clients.Validators;

public class UpdateClientArtistValidator : AbstractValidator<UpdateClientArtistCommand>
{
    public UpdateClientArtistValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.Request.ArtistId)
            .NotEqual(Guid.Empty)
            .When(x => x.Request.ArtistId.HasValue)
            .WithMessage("ArtistId cannot be empty.");
    }
}
