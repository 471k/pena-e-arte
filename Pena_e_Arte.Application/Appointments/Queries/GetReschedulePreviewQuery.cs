using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Services;

namespace Pena_e_Arte.Application.Appointments.Queries;

/// <summary>
/// Whether an appointment can be moved at all, before the caller picks a time (E04b, A02). Uses the
/// same ownership rule and the same <see cref="RescheduleEligibility"/> decision as
/// RescheduleAppointmentHandler: a client sees only their own appointment (404, never 403) and is held
/// to the notice window; staff are not.
/// </summary>
public record GetReschedulePreviewQuery(Guid AppointmentId) : IRequest<ReschedulePreviewResponse>;

public class GetReschedulePreviewHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetReschedulePreviewQuery, ReschedulePreviewResponse>
{
    public async Task<ReschedulePreviewResponse> Handle(GetReschedulePreviewQuery query, CancellationToken ct)
    {
        Appointment appointment = await db.Appointments
            .FirstOrDefaultAsync(a => a.Id == query.AppointmentId, ct)
            ?? throw new NotFoundException(nameof(Appointment), query.AppointmentId);

        bool isClient = currentUser.Role == "client";
        if (isClient)
        {
            Client? me = await db.FindClientForUserAsync(currentUser, ct);
            if (me is null || me.Id != appointment.ClientId)
                throw new NotFoundException(nameof(Appointment), query.AppointmentId);
        }

        // Read for staff too (unlike the save path), only so the response can name the window.
        DepositRule? rule = await db.DepositRules
            .Where(r => r.IsActive)
            .OrderByDescending(r => r.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        RescheduleEligibilityDecision decision = RescheduleEligibility.Decide(
            appointment.Status, isClient, rule, appointment.Date, DateTime.UtcNow);

        return new ReschedulePreviewResponse(
            appointment.Id,
            decision.Allowed,
            decision.Allowed ? null : decision.Block.ToString(),
            RescheduleEligibility.Message(decision, appointment.Status),
            decision.NoticeWindowHours,
            decision.NoticeWindowApplies);
    }
}

public class GetReschedulePreviewValidator : AbstractValidator<GetReschedulePreviewQuery>
{
    public GetReschedulePreviewValidator()
    {
        RuleFor(x => x.AppointmentId).NotEmpty();
    }
}
