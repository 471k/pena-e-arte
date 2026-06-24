using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Studios.Commands;

public record UpdateStudioBrandingCommand(Guid StudioId, bool ShowPlatformBranding)
    : IRequest<StudioResponse>;

public class UpdateStudioBrandingHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<UpdateStudioBrandingCommand, StudioResponse>
{
    public async Task<StudioResponse> Handle(UpdateStudioBrandingCommand command, CancellationToken ct)
    {
        if (command.StudioId != tenant.StudioId)
            throw new NotFoundException(nameof(Domain.Entities.Studio), command.StudioId);

        Domain.Entities.Studio studio = await db.Studios
            .Include(s => s.Subscription)
            .ThenInclude(sub => sub == null ? null : sub.Plan)
            .FirstOrDefaultAsync(s => s.Id == command.StudioId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Studio), command.StudioId);

        // When trying to hide branding, verify the plan allows it
        if (!command.ShowPlatformBranding)
        {
            bool planAllows = studio.Subscription?.Plan?.AllowBrandingRemoval ?? false;
            if (!planAllows)
                throw new BusinessRuleViolationException(
                    "Your current plan does not allow removing platform branding.");
        }

        studio.UpdateBranding(command.ShowPlatformBranding);
        await db.SaveChangesAsync(ct);

        bool allowBrandingRemoval = studio.Subscription?.Plan?.AllowBrandingRemoval ?? false;

        return new StudioResponse(
            studio.Id, studio.Name, studio.Slug, studio.City,
            studio.Latitude, studio.Longitude,
            studio.ShowPlatformBranding,
            allowBrandingRemoval,
            studio.TrialExpiresAt, studio.CreatedAt, studio.IsActive,
            studio.SlugLockedAt);
    }
}

public class UpdateStudioBrandingValidator : AbstractValidator<UpdateStudioBrandingCommand>
{
    public UpdateStudioBrandingValidator()
    {
        RuleFor(x => x.StudioId).NotEmpty();
    }
}
