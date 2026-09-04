using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Services;

namespace Pena_e_Arte.Application.IntakeForms.Commands;

public record SubmitIntakeFormCommand(SubmitIntakeFormRequest Request) : IRequest<IntakeFormResponse>;

public class SubmitIntakeFormHandler(IAppDbContext db, ICurrentTenant tenant, ICurrentUser currentUser, ISender sender)
    : IRequestHandler<SubmitIntakeFormCommand, IntakeFormResponse>
{
    public async Task<IntakeFormResponse> Handle(SubmitIntakeFormCommand command, CancellationToken ct)
    {
        SubmitIntakeFormRequest req = command.Request;

        // Clients cannot submit a form on behalf of another client — always enforce JWT identity.
        Guid clientId = req.ClientId;
        if (currentUser.Role == "client")
        {
            Client client = await db.FindClientForUserAsync(currentUser, ct)
                ?? throw new NotFoundException(nameof(Client), currentUser.UserId);
            clientId = client.Id;
        }

        if (req.AppointmentId.HasValue)
        {
            Appointment appointment = await db.Appointments
                .FirstOrDefaultAsync(a => a.Id == req.AppointmentId.Value, ct)
                ?? throw new NotFoundException(nameof(Appointment), req.AppointmentId.Value);

            if (appointment.ClientId != clientId)
                throw new NotFoundException(nameof(Appointment), req.AppointmentId.Value);
        }

        List<ConsentTemplate> candidates = await db.ConsentTemplates
            .Where(t => t.Kind == ConsentTemplateKind.IntakeFormConsent
                        && t.IsActive
                        && (t.StudioId == tenant.StudioId || t.StudioId == null))
            .ToListAsync(ct);

        ConsentTemplate? consentTemplate = ConsentTemplateResolver.ResolveActive(
            candidates, tenant.StudioId, ConsentTemplateKind.IntakeFormConsent, DateTime.UtcNow);

        IntakeForm form = new()
        {
            StudioId = tenant.StudioId,
            ClientId = clientId,
            AppointmentId = req.AppointmentId,
            FormData = req.FormData,
            FileUrl = req.FileUrl,
            SubmittedAt = DateTime.UtcNow,
            ConsentTemplateId = consentTemplate?.Id,
            ConsentTextSnapshot = consentTemplate?.BodyText,
            ConsentedAt = DateTime.UtcNow
        };

        db.IntakeForms.Add(form);
        await db.SaveChangesAsync(ct);

        await sender.Send(new SendIntakeFormSubmittedNotificationCommand(form.Id), ct);

        return Map(form);
    }

    internal static IntakeFormResponse Map(IntakeForm f) =>
        new(f.Id, f.StudioId, f.ClientId, f.AppointmentId, f.FormData, f.FileUrl, f.SubmittedAt, f.CreatedAt);
}
