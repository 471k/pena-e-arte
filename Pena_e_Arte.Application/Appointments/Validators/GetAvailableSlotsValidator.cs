using FluentValidation;
using Pena_e_Arte.Application.Appointments.Queries;
using Pena_e_Arte.Application.Common;

namespace Pena_e_Arte.Application.Appointments.Validators;

public class GetAvailableSlotsValidator : AbstractValidator<GetAvailableSlotsQuery>
{
    public GetAvailableSlotsValidator(TimeProvider clock)
    {
        RuleFor(x => x.DurationMinutes).InclusiveBetween(30, 480);
        RuleFor(x => x).Custom((q, context) => AvailableSlotsRangeRules.Check(q.From, q.To, clock, context.AddFailure));
    }
}

/// <summary>The range rules both slot-list validators apply (the authenticated and the public one).</summary>
public static class AvailableSlotsRangeRules
{
    public static void Check(DateOnly from, DateOnly to, TimeProvider clock, Action<string, string> addFailure)
    {
        // A day either side of today in UTC covers every studio zone's "today".
        DateOnly todayUtc = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        if (to < from)
            addFailure("To", "To must be on or after From.");
        else if (to.DayNumber - from.DayNumber + 1 > AvailableSlots.MaxRangeDays)
            addFailure("To", $"A range can cover at most {AvailableSlots.MaxRangeDays} days.");
        if (from < todayUtc.AddDays(-1))
            addFailure("From", "From can't be in the past.");
        if (from > todayUtc.AddYears(1))
            addFailure("From", "From can be at most a year ahead.");
    }
}
