using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Services;

namespace Pena_e_Arte.Application.Appointments.Queries;

/// <summary>
/// A session's preparation for the artist (E04a, A03): the DEC03 readiness items from the same
/// derivation the client's reads use, worded for staff, and the deposit's outcome fields. Read-only.
/// An artist reads only a session assigned to them (anything else is 404, so a guessed id confirms
/// nothing); an owner or admin reads any session in the studio, as on the staff appointment page.
/// </summary>
public record GetAppointmentPreparationQuery(Guid AppointmentId) : IRequest<AppointmentPreparationResponse>;

public class GetAppointmentPreparationHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetAppointmentPreparationQuery, AppointmentPreparationResponse>
{
    public async Task<AppointmentPreparationResponse> Handle(GetAppointmentPreparationQuery query, CancellationToken ct)
    {
        Guid id = query.AppointmentId;
        IQueryable<Appointment> target = db.Appointments.Where(a => a.Id == id);

        if (currentUser.Role == "artist")
        {
            Guid? myArtistId = await db.Artists
                .Where(a => a.UserId == currentUser.UserId)
                .Select(a => (Guid?)a.Id)
                .FirstOrDefaultAsync(ct);
            if (myArtistId is null)
                throw new NotFoundException(nameof(Appointment), id);
            target = target.Where(a => a.ArtistId == myArtistId);
        }

        Dictionary<Guid, ReadinessFacts> readiness = await AppointmentReadinessReader.LoadAsync(db, target, ct);
        if (!readiness.TryGetValue(id, out ReadinessFacts? facts))
            throw new NotFoundException(nameof(Appointment), id);

        // Only the fields a deposit's outcome is described from: no session splits, client name, provider
        // reference or card token.
        Payment? payment = await db.Payments
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.AppointmentId == id, ct);
        AppointmentPreparationDepositResponse? deposit = payment is null
            ? null
            : new AppointmentPreparationDepositResponse(
                payment.Amount,
                payment.Status.ToString(),
                payment.Method.ToString(),
                payment.PaidAt,
                payment.Currency,
                payment.RefundedAmount,
                facts.Status.ToString(),
                payment.ReferenceCurrency,
                payment.ReferenceRate);

        return new AppointmentPreparationResponse(
            id,
            AppointmentReadinessReader.ItemiseForStaff(AppointmentReadiness.Derive(facts), facts),
            deposit);
    }
}

public class GetAppointmentPreparationValidator : AbstractValidator<GetAppointmentPreparationQuery>
{
    public GetAppointmentPreparationValidator()
    {
        RuleFor(x => x.AppointmentId).NotEmpty();
    }
}
