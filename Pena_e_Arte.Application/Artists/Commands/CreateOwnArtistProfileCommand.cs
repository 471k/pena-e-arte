using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Utilities;

namespace Pena_e_Arte.Application.Artists.Commands;

public record CreateOwnArtistProfileCommand(CreateOwnArtistProfileRequest Request)
    : IRequest<ArtistResponse>, IQuotaCheckedCommand
{
    public QuotaType QuotaType => QuotaType.Artists;
}

public class CreateOwnArtistProfileValidator : AbstractValidator<CreateOwnArtistProfileCommand>
{
    public CreateOwnArtistProfileValidator()
    {
        RuleFor(x => x.Request.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.HourlyRate).InclusiveBetween(0.01m, 10_000m)
            .When(x => x.Request.HourlyRate is not null);
        RuleFor(x => x.Request.Specializations).MaximumLength(1000)
            .When(x => x.Request.Specializations is not null);
    }
}

public class CreateOwnArtistProfileHandler(
    IAppDbContext db,
    ICurrentTenant tenant,
    ICurrentUser currentUser,
    IPlanLimitService planLimits)
    : IRequestHandler<CreateOwnArtistProfileCommand, ArtistResponse>
{
    public async Task<ArtistResponse> Handle(CreateOwnArtistProfileCommand command, CancellationToken ct)
    {
        CreateOwnArtistProfileRequest req = command.Request;

        bool alreadyHasProfile = await db.Artists.AnyAsync(a => a.UserId == currentUser.UserId, ct);
        if (alreadyHasProfile)
            throw new BusinessRuleViolationException("You already have an artist profile.");

        string baseSlug = SlugHelper.GenerateSlug($"{req.FirstName} {req.LastName}");
        string slug = baseSlug;
        int counter = 2;
        // IgnoreQueryFilters: slug must be globally unique for public portfolio URLs — same
        // rule CreateArtistHandler enforces for an invited artist.
        while (await db.Artists.IgnoreQueryFilters().AnyAsync(a => a.Slug == slug && a.DeletedAt == null, ct))
        {
            slug = $"{baseSlug}-{counter}";
            counter++;
        }

        // No identity.CreateUserAsync call, no AddToRoleAsync, no EnqueueArtistInvite — the
        // owner already has full credentials and a login. This links the existing account,
        // it never creates one.
        Artist artist = new()
        {
            StudioId = tenant.StudioId,
            UserId = currentUser.UserId,
            FirstName = req.FirstName,
            LastName = req.LastName,
            Email = currentUser.Email!,
            Specializations = req.Specializations,
            HourlyRate = req.HourlyRate
        };
        artist.SetSlug(slug);

        db.Artists.Add(artist);
        await db.SaveChangesAsync(ct);

        await planLimits.InvalidateUsageCacheAsync(QuotaType.Artists, ct);

        return CreateArtistHandler.Map(artist);
    }
}
