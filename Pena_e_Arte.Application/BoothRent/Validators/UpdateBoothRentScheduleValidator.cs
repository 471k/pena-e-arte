using FluentValidation;
using Pena_e_Arte.Application.BoothRent.Commands;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.BoothRent.Validators;

public class UpdateBoothRentScheduleValidator : AbstractValidator<UpdateBoothRentScheduleCommand>
{
    public UpdateBoothRentScheduleValidator()
    {
        RuleFor(x => x.Request.AmountFixed).GreaterThan(0);
        RuleFor(x => x.Request.Frequency)
            .Must(f => Enum.TryParse<RentFrequency>(f, out _))
            .WithMessage("Frequency must be Weekly or Monthly.");
    }
}
