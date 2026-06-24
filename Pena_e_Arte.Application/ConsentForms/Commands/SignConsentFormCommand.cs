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

public class SignConsentFormHandler(
    IAppDbContext          db,
    ICurrentTenant         tenant,
    ICurrentUser           currentUser,
    IConsentFormPdfService pdfService,
    IR2Service             r2,
    ISender                sender)
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
            SignedAt      = DateTime.UtcNow
        };

        db.ConsentForms.Add(form);
        await db.SaveChangesAsync(ct);

        // Generate the PDF and upload it to R2 now that we have the form ID.
        string? fileUrl = await TryGeneratePdfAsync(form, appointment, ct);
        if (fileUrl is not null)
        {
            form.FileUrl = fileUrl;
            await db.SaveChangesAsync(ct);
        }

        await sender.Send(new SendConsentFormSignedNotificationCommand(form.Id), ct);

        return Map(form);
    }

    private async Task<string?> TryGeneratePdfAsync(
        ConsentForm form, Appointment appointment, CancellationToken ct)
    {
        try
        {
            Client? client = await db.Clients
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == form.ClientId, ct);

            Artist? artist = await db.Artists
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == appointment.ArtistId, ct);

            Studio? studio = await db.Studios
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == form.StudioId, ct);

            ConsentFormPdfData data = new(
                StudioName:           studio?.Name ?? "Studio",
                ClientFullName:       client is null ? "Client" : $"{client.FirstName} {client.LastName}",
                ArtistFullName:       artist is null ? "Artist" : $"{artist.FirstName} {artist.LastName}",
                AppointmentDate:      appointment.Date,
                SignatureText:        form.SignatureData ?? string.Empty,
                SignedAt:             form.SignedAt ?? DateTime.UtcNow,
                ShowPlatformBranding: studio?.ShowPlatformBranding ?? true);

            byte[]  pdfBytes  = pdfService.Generate(data);
            string  objectKey = $"consent/{form.StudioId}/{form.Id}.pdf";

            await r2.UploadAsync(objectKey, pdfBytes, "application/pdf", ct);
            return r2.GetPublicUrl(objectKey);
        }
        catch
        {
            // PDF generation / upload is best-effort: signing must succeed regardless.
            return null;
        }
    }

    internal static ConsentFormResponse Map(ConsentForm f) =>
        new(f.Id, f.StudioId, f.ClientId, f.AppointmentId, f.FileUrl, f.SignatureData, f.SignedAt, f.CreatedAt);
}
