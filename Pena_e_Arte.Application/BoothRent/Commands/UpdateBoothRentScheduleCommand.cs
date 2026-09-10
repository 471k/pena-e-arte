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

public record UpdateBoothRentScheduleCommand(Guid Id, UpdateBoothRentScheduleRequest Request)
    : IRequest<BoothRentScheduleResponse>;

public class UpdateBoothRentScheduleHandler(IAppDbContext db)
    : IRequestHandler<UpdateBoothRentScheduleCommand, BoothRentScheduleResponse>
{
    public async Task<BoothRentScheduleResponse> Handle(UpdateBoothRentScheduleCommand command, CancellationToken ct)
    {
        BoothRentSchedule schedule = await db.BoothRentSchedules
            .Include(s => s.Artist)
            .FirstOrDefaultAsync(s => s.Id == command.Id, ct)
            ?? throw new NotFoundException(nameof(BoothRentSchedule), command.Id);

        UpdateBoothRentScheduleRequest req = command.Request;

        schedule.AmountFixed = req.AmountFixed;
        schedule.Frequency = Enum.Parse<RentFrequency>(req.Frequency);
        schedule.NextChargeDate = req.NextChargeDate;
        schedule.IsActive = req.IsActive;
        schedule.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        return CreateBoothRentScheduleHandler.Map(schedule, $"{schedule.Artist.FirstName} {schedule.Artist.LastName}");
    }
}
