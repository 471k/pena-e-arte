using FluentValidation;
using Pena_e_Arte.Application.Instagram.Commands;

namespace Pena_e_Arte.Application.Instagram.Validators;

public class ExchangeInstagramCodeValidator : AbstractValidator<ExchangeInstagramCodeCommand>
{
    public ExchangeInstagramCodeValidator()
    {
        RuleFor(x => x.ArtistId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(512);
    }
}
