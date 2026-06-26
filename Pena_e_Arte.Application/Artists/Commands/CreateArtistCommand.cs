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

public record CreateArtistCommand(CreateArtistRequest Request) : IRequest<ArtistResponse>;

public class CreateArtistHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<CreateArtistCommand, ArtistResponse>
{
    public async Task<ArtistResponse> Handle(CreateArtistCommand command, CancellationToken ct)
    {
        CreateArtistRequest req = command.Request;

        bool exists = await db.Artists.AnyAsync(a => a.Email == req.Email, ct);
        if (exists)
            throw new BusinessRuleViolationException($"An artist with email '{req.Email}' already exists in this studio.");

        string baseSlug = SlugHelper.GenerateSlug($"{req.FirstName} {req.LastName}");
        string slug = baseSlug;
        int counter = 2;
        // IgnoreQueryFilters: slug must be globally unique for public portfolio URLs
        while (await db.Artists.IgnoreQueryFilters().AnyAsync(a => a.Slug == slug && a.DeletedAt == null, ct))
        {
            slug = $"{baseSlug}-{counter}";
            counter++;
        }

        Artist artist = new()
        {
            StudioId        = tenant.StudioId,
            FirstName       = req.FirstName,
            LastName        = req.LastName,
            Email           = req.Email,
            Specializations = req.Specializations,
            HourlyRate      = req.HourlyRate
        };
        artist.SetSlug(slug);

        db.Artists.Add(artist);
        await db.SaveChangesAsync(ct);

        return Map(artist);
    }

    internal static ArtistResponse Map(Artist a) =>
        new(a.Id, a.StudioId, a.UserId, a.FirstName, a.LastName, a.Email, a.Specializations, a.HourlyRate, a.PortfolioImages, a.Slug, a.CreatedAt, a.UpdatedAt);
}
