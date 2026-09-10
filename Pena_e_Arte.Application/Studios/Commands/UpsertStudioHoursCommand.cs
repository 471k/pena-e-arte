using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Studios.Commands;

public record StudioHoursEntryDto(DayOfWeek DayOfWeek, TimeSpan StartTime, TimeSpan EndTime, bool IsOpen);

public record UpsertStudioHoursCommand(IReadOnlyList<StudioHoursEntryDto> Entries) : IRequest;

public class UpsertStudioHoursValidator : AbstractValidator<UpsertStudioHoursCommand>
{
    public UpsertStudioHoursValidator()
    {
        RuleFor(x => x.Entries)
            .NotNull()
            .Must(e => e.Count <= 7)
            .WithMessage("A week has at most 7 days.")
            .Must(e => e.Select(entry => entry.DayOfWeek).Distinct().Count() == e.Count)
            .WithMessage("Each day of the week can only appear once.");
        RuleForEach(x => x.Entries).ChildRules(entry =>
        {
            entry.RuleFor(e => e.StartTime).LessThan(e => e.EndTime)
                 .WithMessage("StartTime must be before EndTime.");
        });
    }
}

public class UpsertStudioHoursHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<UpsertStudioHoursCommand>
{
    public async Task Handle(UpsertStudioHoursCommand command, CancellationToken ct)
    {
        List<StudioHours> existing =
            await db.StudioHours
                    .Where(h => h.StudioId == tenant.StudioId)
                    .ToListAsync(ct);

        foreach (StudioHoursEntryDto entry in command.Entries)
        {
            StudioHours? row = existing.FirstOrDefault(h => h.DayOfWeek == entry.DayOfWeek);
            if (row is null)
            {
                db.StudioHours.Add(new StudioHours
                {
                    StudioId = tenant.StudioId,
                    DayOfWeek = entry.DayOfWeek,
                    StartTime = entry.StartTime,
                    EndTime = entry.EndTime,
                    IsOpen = entry.IsOpen,
                });
            }
            else
            {
                row.StartTime = entry.StartTime;
                row.EndTime = entry.EndTime;
                row.IsOpen = entry.IsOpen;
                row.UpdatedAt = DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
