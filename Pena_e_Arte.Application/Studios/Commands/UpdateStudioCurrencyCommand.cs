using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Money;

namespace Pena_e_Arte.Application.Studios.Commands;

/// <summary>
/// Owner-only currency change, mirroring UpdateStudioSlugCommand's shape (lock check -> no-op if
/// unchanged -> save). Unlike the slug, currency locks on money moving rather than on the first
/// change — see StudioCurrencyLock and docs/claude/architecture.md Decisions Log, "Studio
/// currency".
/// </summary>
public record UpdateStudioCurrencyCommand(Guid StudioId, string Currency) : IRequest<StudioResponse>, IAuditableCommand
{
    public string AuditAction => AuditActions.StudioCurrencyChanged;
    public string AuditTargetType => AuditTargetTypes.Studio;
    public Guid AuditTargetId => StudioId;
}

public class UpdateStudioCurrencyHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<UpdateStudioCurrencyCommand, StudioResponse>
{
    public async Task<StudioResponse> Handle(UpdateStudioCurrencyCommand command, CancellationToken ct)
    {
        if (command.StudioId != tenant.StudioId)
            throw new NotFoundException(nameof(Domain.Entities.Studio), command.StudioId);

        Domain.Entities.Studio studio = await db.Studios
            .Include(s => s.Subscription)
            .ThenInclude(sub => sub == null ? null : sub.Plan)
            .FirstOrDefaultAsync(s => s.Id == command.StudioId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Studio), command.StudioId);

        string newCurrency = command.Currency.ToUpperInvariant();

        if (studio.Currency != newCurrency)
        {
            if (await StudioCurrencyLock.IsLockedAsync(db, studio.Id, ct))
            {
                throw new BusinessRuleViolationException(
                    "Your studio's currency can't be changed after the first payment, gift card, "
                    + "package or booth-rent charge has been recorded. Contact support if you need to change it.");
            }

            studio.Currency = newCurrency;
            await db.SaveChangesAsync(ct);
        }

        bool allowBrandingRemoval = studio.Subscription?.Plan?.AllowBrandingRemoval ?? false;
        bool allowApiAccess = studio.Subscription?.Plan?.AllowApiAccess ?? false;

        return new StudioResponse(
            studio.Id, studio.Name, studio.Slug, studio.City,
            studio.Latitude, studio.Longitude,
            studio.ShowPlatformBranding,
            allowBrandingRemoval,
            allowApiAccess,
            studio.TrialExpiresAt, studio.CreatedAt, studio.IsActive,
            studio.SlugLockedAt, studio.PhoneNumber, studio.InstagramHandle, studio.Nipt,
            studio.IsSolo, studio.IsPublished, studio.Timezone,
            AddressLine1: studio.AddressLine1, AddressLine2: studio.AddressLine2, PostalCode: studio.PostalCode,
            // Locked the moment this save completes — even on the very call that just changed it,
            // if that change itself moved money (it never does, but the check stays honest).
            CountryCode: studio.CountryCode, Currency: studio.Currency,
            CurrencyLocked: await StudioCurrencyLock.IsLockedAsync(db, studio.Id, ct));
    }
}

public class UpdateStudioCurrencyValidator : AbstractValidator<UpdateStudioCurrencyCommand>
{
    public UpdateStudioCurrencyValidator()
    {
        RuleFor(x => x.StudioId).NotEmpty();
        RuleFor(x => x.Currency)
            .NotEmpty()
            .Length(3)
            .Must(c => CurrencyCatalog.IsSupported(c))
            .WithMessage("Currency must be a known ISO 4217 code.");
    }
}
