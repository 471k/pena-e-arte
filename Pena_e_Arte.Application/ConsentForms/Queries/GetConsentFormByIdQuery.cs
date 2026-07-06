using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.ConsentForms.Queries;

public record GetConsentFormByIdQuery(Guid Id) : IRequest<ConsentFormDetailResponse>;

public class GetConsentFormByIdHandler(
    IAppDbContext                       db,
    ICurrentUser                        currentUser,
    ILogger<GetConsentFormByIdHandler>  logger)
    : IRequestHandler<GetConsentFormByIdQuery, ConsentFormDetailResponse>
{
    public async Task<ConsentFormDetailResponse> Handle(
        GetConsentFormByIdQuery query, CancellationToken ct)
    {
        Domain.Entities.ConsentForm form = await db.ConsentForms
            .Include(f => f.Client)
            .Include(f => f.Appointment)
                .ThenInclude(a => a.Artist)
            .FirstOrDefaultAsync(f => f.Id == query.Id, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.ConsentForm), query.Id);

        if (currentUser.Role == "client")
        {
            Guid? myId = await db.Clients
                .Where(c => c.UserId == currentUser.UserId)
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync(ct);
            if (myId is null || form.ClientId != myId.Value)
                throw new NotFoundException(nameof(Domain.Entities.ConsentForm), query.Id);
        }

        // Integrity guard — log an anomaly if timestamps are inverted.
        if (form.SignedAt.HasValue && form.SignedAt.Value < form.CreatedAt)
        {
            logger.LogWarning(
                "ConsentForm {FormId} has SignedAt {SignedAt} before CreatedAt {CreatedAt} — investigate UTC mapping",
                form.Id, form.SignedAt, form.CreatedAt);
        }

        Domain.Entities.Artist? artist = form.Appointment.Artist;

        return new ConsentFormDetailResponse(
            Id:              form.Id,
            StudioId:        form.StudioId,
            ClientId:        form.ClientId,
            AppointmentId:   form.AppointmentId,
            FileUrl:         form.FileUrl,
            SignatureData:   form.SignatureData,
            SignedAt:        form.SignedAt,
            CreatedAt:       form.CreatedAt,
            ClientName:      $"{form.Client.FirstName} {form.Client.LastName}".Trim(),
            AppointmentDate: form.Appointment.Date,
            ArtistName:      artist is null ? null : $"{artist.FirstName} {artist.LastName}".Trim(),
            ArtistId:        artist?.Id);
    }
}
