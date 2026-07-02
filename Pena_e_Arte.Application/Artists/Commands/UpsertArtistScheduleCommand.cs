using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Artists.Commands;

public record ScheduleEntryDto(DayOfWeek DayOfWeek, TimeSpan StartTime, TimeSpan EndTime, bool IsAvailable);

public record UpsertArtistScheduleCommand(Guid ArtistId, IReadOnlyList<ScheduleEntryDto> Entries) : IRequest;

public class UpsertArtistScheduleValidator : AbstractValidator<UpsertArtistScheduleCommand>
{
    public UpsertArtistScheduleValidator()
    {
        RuleFor(x => x.ArtistId).NotEmpty();
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

public class UpsertArtistScheduleHandler(IAppDbContext db, ICurrentTenant tenant, ICurrentUser currentUser)
    : IRequestHandler<UpsertArtistScheduleCommand>
{
    public async Task Handle(UpsertArtistScheduleCommand command, CancellationToken ct)
    {
        Artist? artist = await db.Artists.FirstOrDefaultAsync(a => a.Id == command.ArtistId, ct);
        if (artist is null) throw new NotFoundException("Artist", command.ArtistId);

        if (currentUser.Role == "artist" && artist.UserId != currentUser.UserId)
            throw new ForbiddenException();

        List<ArtistSchedule> existing =
            await db.ArtistSchedules
                    .Where(s => s.ArtistId == command.ArtistId)
                    .ToListAsync(ct);

        foreach (ScheduleEntryDto entry in command.Entries)
        {
            ArtistSchedule? row = existing.FirstOrDefault(s => s.DayOfWeek == entry.DayOfWeek);
            if (row is null)
            {
                db.ArtistSchedules.Add(new ArtistSchedule
                {
                    ArtistId    = command.ArtistId,
                    StudioId    = tenant.StudioId,
                    DayOfWeek   = entry.DayOfWeek,
                    StartTime   = entry.StartTime,
                    EndTime     = entry.EndTime,
                    IsAvailable = entry.IsAvailable,
                });
            }
            else
            {
                row.StartTime   = entry.StartTime;
                row.EndTime     = entry.EndTime;
                row.IsAvailable = entry.IsAvailable;
                row.UpdatedAt   = DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
