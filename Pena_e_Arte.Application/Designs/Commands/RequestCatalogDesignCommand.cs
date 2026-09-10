using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Designs.Commands;

/// <summary>Books an appointment from a flash/catalog design: clones the catalog Design +
/// its latest DesignRevision into an independent client-owned design thread (exactly as if the
/// artist had created it manually for this client), then attaches the flash image to the new
/// appointment as a Reference attachment — the booking-visible link. The two rows deliberately
/// don't point at each other any more than a non-catalog booking's Design and Appointment do
/// today (Design has no AppointmentId at all) — see the P1 backlog Group 4 Decisions Log entry.
/// Authenticated-client path only; guest catalog booking is an explicitly flagged open gap
/// (see that same Decisions Log entry) pending confirmation it's actually in scope.</summary>
public record RequestCatalogDesignCommand(Guid CatalogDesignId, CreateAppointmentRequest BookingRequest)
    : IRequest<AppointmentResponse>, IQuotaCheckedCommand
{
    public QuotaType QuotaType => QuotaType.AppointmentsPerMonth;
}

public class RequestCatalogDesignHandler(
    IAppDbContext db,
    ICurrentTenant tenant,
    ICurrentUser currentUser,
    ISlotLocker slotLocker,
    IJobScheduler jobs,
    IRealtimeNotifier realtime,
    ISender sender,
    IPlanLimitService planLimits)
    : IRequestHandler<RequestCatalogDesignCommand, AppointmentResponse>
{
    public async Task<AppointmentResponse> Handle(RequestCatalogDesignCommand command, CancellationToken ct)
    {
        Design catalogDesign = await db.Designs
            .Include(d => d.Revisions)
            .FirstOrDefaultAsync(d =>
                d.Id == command.CatalogDesignId && d.IsCatalogItem && d.ClientId == null, ct)
            ?? throw new NotFoundException(nameof(Design), command.CatalogDesignId);

        DesignRevision? latestRevision = catalogDesign.Revisions
            .OrderByDescending(r => r.VersionNumber)
            .FirstOrDefault();

        Guid clientId;
        if (currentUser.Role == "client")
        {
            Client client = await db.FindClientForUserAsync(currentUser, ct)
                ?? throw new NotFoundException(nameof(Client), currentUser.UserId);
            clientId = client.Id;
        }
        else
        {
            clientId = command.BookingRequest.ClientId;
        }

        // Seeds an independent design-approval thread for the artist, exactly as if they'd
        // created it manually for this client — no new schema, no link back to the catalog
        // original (matches how every other Design already works).
        Design clonedDesign = new()
        {
            StudioId = tenant.StudioId,
            ClientId = clientId,
            ArtistId = catalogDesign.ArtistId,
            Title = $"{catalogDesign.Title} (flash booking)",
            Description = catalogDesign.Description,
        };
        db.Designs.Add(clonedDesign);

        if (latestRevision is not null)
        {
            db.DesignRevisions.Add(new DesignRevision
            {
                StudioId = tenant.StudioId,
                DesignId = clonedDesign.Id,
                VersionNumber = 1,
                FileUrl = latestRevision.FileUrl,
                Notes = "Cloned from flash catalog booking.",
            });
        }

        // The booking-visible link: attach the flash image as a Reference attachment on the
        // new appointment, unless the client's own request already includes it.
        CreateAppointmentRequest bookingRequest = command.BookingRequest with { ClientId = clientId };
        if (latestRevision is not null &&
            !(bookingRequest.Images ?? []).Any(i => i.Url == latestRevision.FileUrl))
        {
            List<AppointmentImageRequest> images = [.. bookingRequest.Images ?? []];
            images.Add(new AppointmentImageRequest(latestRevision.FileUrl, nameof(AppointmentAttachmentCategory.Reference)));
            bookingRequest = bookingRequest with { Images = images };
        }

        // CreateAppointmentCoreAsync's own SaveChangesAsync calls persist the clonedDesign +
        // DesignRevision added above in the same transaction as the Appointment — no separate
        // save needed here.
        return await CreateAppointmentHandler.CreateAppointmentCoreAsync(
            db, tenant.StudioId, clientId, bookingRequest, slotLocker, jobs, realtime, sender, planLimits, ct);
    }
}
