using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Clients.Queries;

/// <summary>Client-initiated self-service "export my data" (§Phase C).</summary>
public record ExportMyDataQuery : IRequest<ClientDataExportResponse>;

public class ExportMyDataHandler(IAppDbContext db, ICurrentUser currentUser, IR2Service r2)
    : IRequestHandler<ExportMyDataQuery, ClientDataExportResponse>
{
    public async Task<ClientDataExportResponse> Handle(ExportMyDataQuery query, CancellationToken ct)
    {
        // Same cross-tenant fan-out as Phase A/D (§3.3) — "my data" means every studio
        // relationship, not just the active one. Approved usage #29.
        List<Client> clients = await db.FindAllClientRecordsForUserAsync(currentUser.UserId, ct);
        if (clients.Count == 0)
            throw new NotFoundException(nameof(Client), currentUser.UserId);

        List<Guid> clientIds = clients.Select(c => c.Id).ToList();
        List<Guid> studioIds = clients.Select(c => c.StudioId).Distinct().ToList();

        // Studio has no tenant query filter (it IS the tenant) — no IgnoreQueryFilters needed.
        List<Studio> studios = await db.Studios
            .Where(s => studioIds.Contains(s.Id))
            .ToListAsync(ct);

        List<ClientProfile> profiles = await db.ClientProfiles
            .IgnoreQueryFilters().Where(p => clientIds.Contains(p.ClientId)).ToListAsync(ct);
        List<Appointment> appointments = await db.Appointments
            .IgnoreQueryFilters().Where(a => clientIds.Contains(a.ClientId)).ToListAsync(ct);
        List<ConsentForm> forms = await db.ConsentForms
            .IgnoreQueryFilters().Where(f => clientIds.Contains(f.ClientId)).ToListAsync(ct);
        List<TattooRecord> tattoos = await db.TattooRecords
            .IgnoreQueryFilters().Where(t => clientIds.Contains(t.ClientId)).ToListAsync(ct);
        List<Payment> payments = await db.Payments
            .IgnoreQueryFilters().Where(p => clientIds.Contains(p.ClientId)).ToListAsync(ct);

        List<ClientDataExportStudioSection> sections = [];
        foreach (Client client in clients)
        {
            Studio? studio = studios.FirstOrDefault(s => s.Id == client.StudioId);
            ClientProfile? profile = profiles.FirstOrDefault(p => p.ClientId == client.Id);

            List<ClientDataExportAppointment> clientAppointments = appointments
                .Where(a => a.ClientId == client.Id)
                .Select(a =>
                {
                    Payment? payment = payments.FirstOrDefault(p => p.AppointmentId == a.Id);
                    return new ClientDataExportAppointment(
                        a.Id, a.Date, a.EndDate, a.Status.ToString(),
                        payment?.Amount, payment?.Status.ToString());
                })
                .ToList();

            List<ClientDataExportConsentForm> clientForms = [];
            foreach (ConsentForm form in forms.Where(f => f.ClientId == client.Id))
            {
                string? signedUrl = form.FileUrl is not null && r2.IsR2Url(form.FileUrl)
                    ? await r2.GeneratePresignedReadUrlAsync(form.FileUrl, ct)
                    : form.FileUrl;
                clientForms.Add(new ClientDataExportConsentForm(
                    form.Id, form.AppointmentId, form.SignedAt, signedUrl));
            }

            List<ClientDataExportTattooRecord> clientTattoos = tattoos
                .Where(t => t.ClientId == client.Id)
                .Select(t => new ClientDataExportTattooRecord(
                    t.Id, t.Description, t.BodyLocation, t.CompletedAt, t.PhotoUrls))
                .ToList();

            sections.Add(new ClientDataExportStudioSection(
                client.StudioId,
                studio?.Name ?? "Unknown studio",
                new ClientDataExportProfile(
                    client.FirstName, client.LastName, client.Email, client.Phone, client.CreatedAt,
                    profile?.DateOfBirth, profile?.Allergies, profile?.MedicalNotes),
                clientAppointments,
                clientForms,
                clientTattoos));
        }

        return new ClientDataExportResponse(sections);
    }
}
