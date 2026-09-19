using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Public.Queries;

public record GetPublicServicesQuery(string StudioSlug) : IRequest<List<PublicServiceResponse>>;

public class GetPublicServicesHandler(IAppDbContext db)
    : IRequestHandler<GetPublicServicesQuery, List<PublicServiceResponse>>
{
    public async Task<List<PublicServiceResponse>> Handle(GetPublicServicesQuery query, CancellationToken ct)
    {
        // Approved: public/anonymous studio-slug resolution — shared via PublicStudioLookupExtensions.
        Studio studio = await db.GetPublishedStudioBySlugAsync(query.StudioSlug, ct)
            ?? throw new NotFoundException(nameof(Studio), query.StudioSlug);

        // Approved: public/anonymous — same IgnoreQueryFilters + explicit studioId predicate
        // pattern every other public/guest-booking query in this handler group already uses,
        // since CreateAppointmentCoreAsync itself runs unauthenticated on this path too.
        return await db.Services
            .IgnoreQueryFilters()
            .Where(s => s.StudioId == studio.Id && s.DeletedAt == null && s.IsActive)
            .OrderBy(s => s.Name)
            .Select(s => new PublicServiceResponse(
                s.Id, s.Name, s.Description, s.DurationMinutes, s.Price, s.DepositAmount))
            .ToListAsync(ct);
    }
}
