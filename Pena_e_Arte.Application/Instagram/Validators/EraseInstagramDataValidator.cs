using FluentValidation;
using Pena_e_Arte.Application.Instagram.Commands;

namespace Pena_e_Arte.Application.Instagram.Validators;

public class EraseInstagramDataValidator : AbstractValidator<EraseInstagramDataCommand>
{
    public EraseInstagramDataValidator()
    {
        RuleFor(x => x.InstagramUserId).NotEmpty().MaximumLength(64);
    }
}
