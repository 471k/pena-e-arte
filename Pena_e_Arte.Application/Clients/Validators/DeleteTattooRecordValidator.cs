using FluentValidation;
using Pena_e_Arte.Application.Clients.Commands;

namespace Pena_e_Arte.Application.Clients.Validators;

public class DeleteTattooRecordValidator : AbstractValidator<DeleteTattooRecordCommand>
{
    public DeleteTattooRecordValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
    }
}
