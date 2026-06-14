using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Artists.Commands;

public record UpdateArtistCommand(Guid Id, UpdateArtistRequest Request) : IRequest<ArtistResponse>;

public class UpdateArtistHandler(IAppDbContext db)
    : IRequestHandler<UpdateArtistCommand, ArtistResponse>
{
    public async Task<ArtistResponse> Handle(UpdateArtistCommand command, CancellationToken ct)
    {
        UpdateArtistRequest req = command.Request;

        Artist? artist = await db.Artists.FirstOrDefaultAsync(a => a.Id == command.Id, ct);
        if (artist is null)
            throw new NotFoundException(nameof(Artist), command.Id);

        bool emailTaken = await db.Artists.AnyAsync(a => a.Email == req.Email && a.Id != command.Id, ct);
        if (emailTaken)
            throw new BusinessRuleViolationException($"An artist with email '{req.Email}' already exists in this studio.");

        artist.FirstName       = req.FirstName;
        artist.LastName        = req.LastName;
        artist.Email           = req.Email;
        artist.Specializations = req.Specializations;
        artist.HourlyRate      = req.HourlyRate;
        artist.UpdatedAt       = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        return CreateArtistHandler.Map(artist);
    }
}
