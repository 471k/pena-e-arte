using FluentValidation;
using Pena_e_Arte.Contracts.Requests;

namespace Pena_e_Arte.Application.Traffic.Validators;

public class RecordTrafficBeaconValidator : AbstractValidator<RecordTrafficBeaconRequest>
{
    public RecordTrafficBeaconValidator()
    {
        RuleFor(x => x.Path)
            .NotEmpty()
            .MaximumLength(200)
            .WithMessage("Path is required and must be at most 200 characters.");
    }
}
