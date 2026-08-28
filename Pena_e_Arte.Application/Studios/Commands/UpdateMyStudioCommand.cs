using System.Text.RegularExpressions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Studios.Commands;

public record UpdateMyStudioCommand(UpdateStudioRequest Request) : IRequest<StudioResponse>;

public class UpdateMyStudioHandler(IAppDbContext db, ICurrentTenant tenant, ILogger<UpdateMyStudioHandler> logger)
    : IRequestHandler<UpdateMyStudioCommand, StudioResponse>
{
    public async Task<StudioResponse> Handle(UpdateMyStudioCommand command, CancellationToken ct)
    {
        Domain.Entities.Studio studio = await db.Studios
            .FirstOrDefaultAsync(s => s.Id == tenant.StudioId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Studio), tenant.StudioId);

        studio.Name = command.Request.Name;
        studio.City = command.Request.City;
        studio.Latitude = command.Request.Latitude;
        studio.Longitude = command.Request.Longitude;
        studio.PhoneNumber = string.IsNullOrWhiteSpace(command.Request.PhoneNumber)
                                  ? null : command.Request.PhoneNumber.Trim();

        // InstagramHandle is deliberately NOT written here anymore. The frontend form no
        // longer collects it (Instagram is now managed via SocialLinksCard →
        // UpdateSocialHandleCommand, writing to SocialAccountLink), so
        // UpdateStudioRequest.InstagramHandle is always null/omitted on every real call —
        // writing it unconditionally would silently null out the legacy column (kept only
        // for its one-time SocialAccountLink backfill history) on every unrelated save.

        if (!string.IsNullOrWhiteSpace(command.Request.Nipt))
        {
            string normalizedNipt = command.Request.Nipt.Trim().ToUpperInvariant();

            // Fetch candidates first, then compare owner email in memory — mirrors the
            // RegisterUserCommand owner-email cross-check pattern (OrdinalIgnoreCase does
            // not reliably translate to SQL for every provider).
            Domain.Entities.Studio? conflictingStudio = await db.Studios.IgnoreQueryFilters()
                .Where(s => s.Id != studio.Id && s.Nipt == normalizedNipt && s.IsActive)
                .FirstOrDefaultAsync(ct);
            if (conflictingStudio is not null &&
                !string.Equals(conflictingStudio.OwnerEmail, studio.OwnerEmail, StringComparison.OrdinalIgnoreCase))
            {
                throw new DuplicateNiptException();
            }

            studio.Nipt = normalizedNipt;
        }

        // (0, 0) as "location not yet set" is a deliberate, cheap sentinel — real-world
        // coordinates landing exactly on Null Island are not a realistic false-positive
        // for this product's userbase. One-way transition: this handler never un-publishes.
        if (studio.IsSolo && !studio.IsPublished &&
            !string.IsNullOrWhiteSpace(studio.City) &&
            (studio.Latitude != 0 || studio.Longitude != 0))
        {
            studio.IsPublished = true;
            logger.LogInformation("Solo studio {@StudioId} auto-published on real location", studio.Id);
        }

        await db.SaveChangesAsync(ct);

        return new StudioResponse(
            studio.Id, studio.Name, studio.Slug, studio.City,
            studio.Latitude, studio.Longitude,
            studio.ShowPlatformBranding,
            AllowBrandingRemoval: false,
            studio.TrialExpiresAt, studio.CreatedAt, studio.IsActive,
            studio.SlugLockedAt, studio.PhoneNumber, studio.InstagramHandle, studio.Nipt,
            studio.IsSolo, studio.IsPublished);
    }
}

public class UpdateMyStudioValidator : AbstractValidator<UpdateMyStudioCommand>
{
    private static readonly Regex NiptFormat = new(@"^[A-Z]\d{8}[A-Z]$", RegexOptions.Compiled);
    private static readonly Regex E164Format = new(@"^\+[1-9]\d{1,14}$", RegexOptions.Compiled);

    public UpdateMyStudioValidator()
    {
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.City).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.Latitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.Request.Longitude).InclusiveBetween(-180, 180);
        RuleFor(x => x.Request.PhoneNumber)
            .Matches(E164Format)
            .WithMessage("Phone must be in international format, e.g. +351912345678.")
            .When(x => !string.IsNullOrWhiteSpace(x.Request.PhoneNumber));
        RuleFor(x => x.Request.Nipt)
            .Length(10)
            .Must(n => NiptFormat.IsMatch(n!.Trim().ToUpperInvariant()))
            .WithMessage("NIPT must be 10 characters: a letter, 8 digits, then a letter (e.g. L01234567A).")
            .When(x => !string.IsNullOrWhiteSpace(x.Request.Nipt));
    }
}
