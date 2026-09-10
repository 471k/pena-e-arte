using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.BoothRent.Commands;

public record CreateBoothRentScheduleCommand(CreateBoothRentScheduleRequest Request)
    : IRequest<BoothRentScheduleResponse>;

public class CreateBoothRentScheduleHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<CreateBoothRentScheduleCommand, BoothRentScheduleResponse>
{
    public async Task<BoothRentScheduleResponse> Handle(CreateBoothRentScheduleCommand command, CancellationToken ct)
    {
        CreateBoothRentScheduleRequest req = command.Request;

        Artist artist = await db.Artists
            .FirstOrDefaultAsync(a => a.Id == req.ArtistId, ct)
            ?? throw new NotFoundException(nameof(Artist), req.ArtistId);

        BoothRentSchedule schedule = new()
        {
            StudioId = tenant.StudioId,
            ArtistId = artist.Id,
            AmountFixed = req.AmountFixed,
            Frequency = Enum.Parse<RentFrequency>(req.Frequency),
            NextChargeDate = req.NextChargeDate,
            IsActive = req.IsActive,
        };

        db.BoothRentSchedules.Add(schedule);
        await db.SaveChangesAsync(ct);

        return Map(schedule, $"{artist.FirstName} {artist.LastName}");
    }

    internal static BoothRentScheduleResponse Map(BoothRentSchedule s, string? artistName = null) => new(
        s.Id, s.StudioId, s.ArtistId, artistName,
        s.AmountFixed, s.Frequency.ToString(), s.NextChargeDate, s.IsActive,
        s.CreatedAt, s.UpdatedAt);
}
