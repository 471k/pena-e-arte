using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Studios.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Studios.Queries;

public record GetMyBookingSettingsQuery : IRequest<BookingSettingsResponse>;

public class GetMyBookingSettingsHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<GetMyBookingSettingsQuery, BookingSettingsResponse>
{
    public async Task<BookingSettingsResponse> Handle(GetMyBookingSettingsQuery query, CancellationToken ct)
    {
        Studio studio = await db.Studios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == tenant.StudioId, ct)
            ?? throw new NotFoundException(nameof(Studio), tenant.StudioId);
        return new BookingSettingsResponse(studio.BookingSlotStepMinutes, BookingSlotStep.AllowedMinutes);
    }
}
