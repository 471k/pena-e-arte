using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Studios.Commands;

public record UpdateStudioBrandingCommand(Guid StudioId, bool ShowPlatformBranding)
    : IRequest<StudioResponse>;

public class UpdateStudioBrandingHandler(IAppDbContext db)
    : IRequestHandler<UpdateStudioBrandingCommand, StudioResponse>
{
    public async Task<StudioResponse> Handle(UpdateStudioBrandingCommand command, CancellationToken ct)
    {
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

        studio.ShowPlatformBranding = command.ShowPlatformBranding;
        await db.SaveChangesAsync(ct);

        return new StudioResponse(
            studio.Id, studio.Name, studio.Slug, studio.City,
            studio.Latitude, studio.Longitude,
            studio.ShowPlatformBranding,
            studio.TrialExpiresAt, studio.CreatedAt);
    }
}

public class UpdateStudioBrandingValidator : AbstractValidator<UpdateStudioBrandingCommand>
{
    public UpdateStudioBrandingValidator()
    {
        RuleFor(x => x.StudioId).NotEmpty();
    }
}
