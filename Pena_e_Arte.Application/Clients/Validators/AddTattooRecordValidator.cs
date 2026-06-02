using FluentValidation;
using Pena_e_Arte.Application.Clients.Commands;

namespace Pena_e_Arte.Application.Clients.Validators;

public class AddTattooRecordValidator : AbstractValidator<AddTattooRecordCommand>
{
    public AddTattooRecordValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.Request.ArtistId).NotEmpty();
        RuleFor(x => x.Request.Description).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.Request.BodyLocation).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.PhotoUrls).NotNull();
        RuleForEach(x => x.Request.PhotoUrls).NotEmpty().MaximumLength(2048);
        RuleFor(x => x.Request.CompletedAt).LessThanOrEqualTo(_ => DateTime.UtcNow);
    }
}
