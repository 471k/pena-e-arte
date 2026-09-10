using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.BoothRent.Queries;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.BoothRent.Commands;

public record MarkBoothRentChargeSettledCommand(Guid Id, MarkBoothRentChargeSettledRequest Request)
    : IRequest<BoothRentChargeResponse>;

public class MarkBoothRentChargeSettledHandler(IAppDbContext db)
    : IRequestHandler<MarkBoothRentChargeSettledCommand, BoothRentChargeResponse>
{
    public async Task<BoothRentChargeResponse> Handle(MarkBoothRentChargeSettledCommand command, CancellationToken ct)
    {
        BoothRentCharge charge = await db.BoothRentCharges
            .Include(c => c.Artist)
            .FirstOrDefaultAsync(c => c.Id == command.Id, ct)
            ?? throw new NotFoundException(nameof(BoothRentCharge), command.Id);

        charge.IsSettled = true;
        charge.SettledAt = DateTime.UtcNow;
        charge.SettledNote = command.Request.SettledNote;
        charge.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        return GetBoothRentChargesHandler.Map(charge, $"{charge.Artist.FirstName} {charge.Artist.LastName}");
    }
}
