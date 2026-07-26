using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Artists.Commands;

public record AddArtistTimeOffCommand(
    Guid ArtistId,
    DateTime StartDate,
    DateTime EndDate,
    string Reason) : IRequest<Guid>;

public class AddArtistTimeOffValidator : AbstractValidator<AddArtistTimeOffCommand>
{
    public AddArtistTimeOffValidator()
    {
        RuleFor(x => x.ArtistId).NotEmpty();
        RuleFor(x => x.StartDate).NotEmpty();
        RuleFor(x => x.EndDate)
            .NotEmpty()
            .GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("EndDate must be on or after StartDate.");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public class AddArtistTimeOffHandler(IAppDbContext db, ICurrentTenant tenant, ICurrentUser currentUser)
    : IRequestHandler<AddArtistTimeOffCommand, Guid>
{
    public async Task<Guid> Handle(AddArtistTimeOffCommand command, CancellationToken ct)
    {
        Artist? artist = await db.Artists.FirstOrDefaultAsync(a => a.Id == command.ArtistId, ct);
        if (artist is null) throw new NotFoundException("Artist", command.ArtistId);

        if (currentUser.Role == "artist" && artist.UserId != currentUser.UserId)
            throw new ForbiddenException();

        DateTime startDate = command.StartDate.Date;
        DateTime endDate = command.EndDate.Date;

        bool overlaps = await db.ArtistTimeOffs.AnyAsync(t =>
            t.ArtistId == command.ArtistId
            && t.StartDate <= endDate
            && t.EndDate >= startDate, ct);

        if (overlaps)
            throw new BusinessRuleViolationException(
                "This time-off period overlaps with an existing one for this artist.");

        ArtistTimeOff timeOff = new()
        {
            ArtistId = command.ArtistId,
            StudioId = tenant.StudioId,
            StartDate = startDate,
            EndDate = endDate,
            Reason = command.Reason,
        };

        db.ArtistTimeOffs.Add(timeOff);
        await db.SaveChangesAsync(ct);
        return timeOff.Id;
    }
}
