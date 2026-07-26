using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Utilities;

namespace Pena_e_Arte.Application.Artists.Commands;

public record UpdateArtistCommand(Guid Id, UpdateArtistRequest Request) : IRequest<ArtistResponse>;

public class UpdateArtistHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<UpdateArtistCommand, ArtistResponse>
{
    public async Task<ArtistResponse> Handle(UpdateArtistCommand command, CancellationToken ct)
    {
        UpdateArtistRequest req = command.Request;

        Artist? artist = await db.Artists
            .Include(a => a.Portfolio)
            .FirstOrDefaultAsync(a => a.Id == command.Id, ct);
        if (artist is null)
            throw new NotFoundException(nameof(Artist), command.Id);

        if (currentUser.Role == "artist" && artist.UserId != currentUser.UserId)
            throw new ForbiddenException();

        bool emailTaken = await db.Artists.AnyAsync(a => a.Email == req.Email && a.Id != command.Id, ct);
        if (emailTaken)
            throw new BusinessRuleViolationException($"An artist with email '{req.Email}' already exists in this studio.");

        artist.FirstName = req.FirstName;
        artist.LastName = req.LastName;
        artist.Email = req.Email;
        artist.Specializations = req.Specializations;
        artist.HourlyRate = req.HourlyRate;
        artist.UpdatedAt = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(req.Slug))
        {
            string slug = SlugHelper.GenerateSlug(req.Slug);
            // IgnoreQueryFilters: slug must be globally unique for public portfolio URLs
            bool taken = await db.Artists
                .IgnoreQueryFilters()
                .AnyAsync(a => a.Slug == slug && a.Id != command.Id && a.DeletedAt == null, ct);
            if (taken)
                throw new BusinessRuleViolationException($"The slug '{slug}' is already in use.");
            artist.SetSlug(slug);
        }

        await db.SaveChangesAsync(ct);

        return CreateArtistHandler.Map(artist);
    }
}
