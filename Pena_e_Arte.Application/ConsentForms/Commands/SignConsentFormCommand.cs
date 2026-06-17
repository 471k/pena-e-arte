using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.ConsentForms.Commands;

public record SignConsentFormCommand(SignConsentFormRequest Request) : IRequest<ConsentFormResponse>;

public class SignConsentFormHandler(IAppDbContext db, ICurrentTenant tenant, ICurrentUser currentUser, ISender sender)
    : IRequestHandler<SignConsentFormCommand, ConsentFormResponse>
{
    public async Task<ConsentFormResponse> Handle(SignConsentFormCommand command, CancellationToken ct)
    {
        SignConsentFormRequest req = command.Request;

        Appointment appointment = await db.Appointments
            .FirstOrDefaultAsync(a => a.Id == req.AppointmentId, ct)
            ?? throw new NotFoundException(nameof(Appointment), req.AppointmentId);

        // Clients may only sign a consent form for their own appointment —
        // ownership resolved (and healed) through Client.UserId / email.
        if (currentUser.Role == "client")
        {
            Client? me = await db.FindClientForUserAsync(currentUser, ct);
            if (me is null || me.Id != appointment.ClientId)
                throw new NotFoundException(nameof(Appointment), req.AppointmentId);
        }

        bool alreadySigned = await db.ConsentForms
            .AnyAsync(c => c.AppointmentId == req.AppointmentId, ct);

        if (alreadySigned) throw new ConsentFormAlreadySignedException();

        ConsentForm form = new()
        {
            StudioId      = tenant.StudioId,
            ClientId      = appointment.ClientId,
            AppointmentId = appointment.Id,
            SignatureData = req.SignatureData,
            FileUrl       = req.FileUrl,
            SignedAt      = DateTime.UtcNow
        };

        db.ConsentForms.Add(form);
        await db.SaveChangesAsync(ct);

        await sender.Send(new SendConsentFormSignedNotificationCommand(form.Id), ct);

        return Map(form);
    }

    internal static ConsentFormResponse Map(ConsentForm f) =>
        new(f.Id, f.StudioId, f.ClientId, f.AppointmentId, f.FileUrl, f.SignatureData, f.SignedAt, f.CreatedAt);
}
