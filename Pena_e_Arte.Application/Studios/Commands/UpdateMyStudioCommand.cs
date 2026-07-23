using System.Text.RegularExpressions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Studios.Commands;

public record UpdateMyStudioCommand(UpdateStudioRequest Request) : IRequest<StudioResponse>;

public class UpdateMyStudioHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<UpdateMyStudioCommand, StudioResponse>
{
    public async Task<StudioResponse> Handle(UpdateMyStudioCommand command, CancellationToken ct)
    {
        Domain.Entities.Studio studio = await db.Studios
            .FirstOrDefaultAsync(s => s.Id == tenant.StudioId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Studio), tenant.StudioId);

        studio.Name            = command.Request.Name;
        studio.City            = command.Request.City;
        studio.Latitude        = command.Request.Latitude;
        studio.Longitude       = command.Request.Longitude;
        studio.PhoneNumber     = string.IsNullOrWhiteSpace(command.Request.PhoneNumber)
                                  ? null : command.Request.PhoneNumber.Trim();
        studio.InstagramHandle = string.IsNullOrWhiteSpace(command.Request.InstagramHandle)
                                  ? null : command.Request.InstagramHandle.Trim().TrimStart('@');

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

        await db.SaveChangesAsync(ct);

        return new StudioResponse(
            studio.Id, studio.Name, studio.Slug, studio.City,
            studio.Latitude, studio.Longitude,
            studio.ShowPlatformBranding,
            AllowBrandingRemoval: false,
            studio.TrialExpiresAt, studio.CreatedAt, studio.IsActive,
            studio.SlugLockedAt, studio.PhoneNumber, studio.InstagramHandle, studio.Nipt);
    }
}

public class UpdateMyStudioValidator : AbstractValidator<UpdateMyStudioCommand>
{
    private static readonly Regex NiptFormat = new(@"^[A-Z]\d{8}[A-Z]$", RegexOptions.Compiled);

    public UpdateMyStudioValidator()
    {
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.City).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.Latitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.Request.Longitude).InclusiveBetween(-180, 180);
        RuleFor(x => x.Request.Nipt)
            .Length(10)
            .Must(n => NiptFormat.IsMatch(n!.Trim().ToUpperInvariant()))
            .WithMessage("NIPT must be 10 characters: a letter, 8 digits, then a letter (e.g. L01234567A).")
            .When(x => !string.IsNullOrWhiteSpace(x.Request.Nipt));
    }
}
