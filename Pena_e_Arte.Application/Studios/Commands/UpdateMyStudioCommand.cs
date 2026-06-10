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

        studio.Name      = command.Request.Name;
        studio.City      = command.Request.City;
        studio.Latitude  = command.Request.Latitude;
        studio.Longitude = command.Request.Longitude;

        await db.SaveChangesAsync(ct);

        return new StudioResponse(
            studio.Id, studio.Name, studio.Slug, studio.City,
            studio.Latitude, studio.Longitude,
            studio.ShowPlatformBranding,
            AllowBrandingRemoval: false,
            studio.TrialExpiresAt, studio.CreatedAt);
    }
}

public class UpdateMyStudioValidator : AbstractValidator<UpdateMyStudioCommand>
{
    public UpdateMyStudioValidator()
    {
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.City).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.Latitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.Request.Longitude).InclusiveBetween(-180, 180);
    }
}
