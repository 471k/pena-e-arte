using FluentValidation;
using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Clients.Validators;

public class UpdateTattooRecordValidator : AbstractValidator<UpdateTattooRecordCommand>
{
    public UpdateTattooRecordValidator(IR2Service r2)
    {
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Request.Description).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.Request.BodyLocation).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.PhotoUrls).NotNull();
        RuleForEach(x => x.Request.PhotoUrls)
            .NotEmpty()
            .MaximumLength(2048)
            .Must(r2.IsR2Url)
            .WithMessage("Each photo URL must reference a valid storage URL.");
        RuleFor(x => x.Request.CompletedAt).LessThanOrEqualTo(_ => DateTime.UtcNow);
    }
}
