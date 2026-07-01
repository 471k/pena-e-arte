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

public class CreateArtistHandler(
    IAppDbContext    db,
    ICurrentTenant   tenant,
    IIdentityService identity,
    IJobScheduler    scheduler)
    : IRequestHandler<CreateArtistCommand, ArtistResponse>
{
    public async Task<ArtistResponse> Handle(CreateArtistCommand command, CancellationToken ct)
    {
        CreateArtistRequest req = command.Request;

        bool exists = await db.Artists.AnyAsync(a => a.Email == req.Email, ct);
        if (exists)
            throw new BusinessRuleViolationException($"An artist with email '{req.Email}' already exists in this studio.");

        string baseSlug = SlugHelper.GenerateSlug($"{req.FirstName} {req.LastName}");
        string slug     = baseSlug;
        int    counter  = 2;
        // IgnoreQueryFilters: slug must be globally unique for public portfolio URLs
        while (await db.Artists.IgnoreQueryFilters().AnyAsync(a => a.Slug == slug && a.DeletedAt == null, ct))
        {
            slug = $"{baseSlug}-{counter}";
            counter++;
        }

        // Create the Identity login account for the artist
        string tempPassword = $"Tmp!{Guid.NewGuid():N}";
        (bool created, Guid userId, string[] errors) =
            await identity.CreateUserAsync(req.Email, tempPassword, "artist", tenant.StudioId, req.FirstName);

        if (!created)
        {
            bool emailTaken = errors.Any(e => e.Contains("already taken", StringComparison.OrdinalIgnoreCase));
            if (!emailTaken)
                throw new BusinessRuleViolationException($"Failed to create artist account: {string.Join(", ", errors)}");

            // An Identity user exists with no linked artist (orphaned from a previous failed attempt).
            // Recover by reusing the existing user's ID.
            Guid? existingId = await identity.GetUserIdByEmailAsync(req.Email, ct);
            if (existingId is null)
                throw new BusinessRuleViolationException($"The email '{req.Email}' is already registered to another account. Each artist must have a unique email address.");

            userId = existingId.Value;
        }

        Artist artist = new()
        {
            StudioId        = tenant.StudioId,
            UserId          = userId,
            FirstName       = req.FirstName,
            LastName        = req.LastName,
            Email           = req.Email,
            Specializations = req.Specializations,
            HourlyRate      = req.HourlyRate
        };
        artist.SetSlug(slug);

        db.Artists.Add(artist);
        await db.SaveChangesAsync(ct);

        // Fire-and-forget via Hangfire so the HTTP response returns immediately
        scheduler.EnqueueArtistInvite(req.Email, req.FirstName, tenant.StudioId);

        return Map(artist);
    }

    internal static ArtistResponse Map(Artist a) =>
        new(a.Id, a.StudioId, a.UserId, a.FirstName, a.LastName, a.Email, a.Specializations, a.HourlyRate,
            a.IsActive, a.AvatarUrl,
            a.Portfolio.OrderByDescending(p => p.CreatedAt).Select(p => p.ImageUrl).ToList(),
            a.Slug, a.CreatedAt, a.UpdatedAt);
}
