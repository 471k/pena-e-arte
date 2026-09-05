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
using Pena_e_Arte.Domain.ValueObjects;

namespace Pena_e_Arte.Application.Appointments.Commands;

public record CreateAppointmentCommand(CreateAppointmentRequest Request)
    : IRequest<AppointmentResponse>, IQuotaCheckedCommand
{
    public QuotaType QuotaType => QuotaType.AppointmentsPerMonth;
}

public class CreateAppointmentHandler(
    IAppDbContext db,
    ICurrentTenant tenant,
    ICurrentUser currentUser,
    ISlotLocker slotLocker,
    IJobScheduler jobs,
    IRealtimeNotifier realtime,
    ISender sender,
    IPlanLimitService planLimits)
    : IRequestHandler<CreateAppointmentCommand, AppointmentResponse>
{
    public async Task<AppointmentResponse> Handle(CreateAppointmentCommand command, CancellationToken ct)
    {
        CreateAppointmentRequest req = command.Request;

        // Clients cannot book on behalf of another client — always enforce JWT identity.
        // The JWT carries the IdentityUser id; resolve it to the tenant's Client record.
        Guid clientId;
        if (currentUser.Role == "client")
        {
            Client client = await db.FindClientForUserAsync(currentUser, ct)
                ?? throw new NotFoundException(nameof(Client), currentUser.UserId);
            clientId = client.Id;
        }
        else
        {
            clientId = req.ClientId;
        }

        return await CreateAppointmentCoreAsync(
            db, tenant.StudioId, clientId, req, slotLocker, jobs, realtime, sender, planLimits, ct);
    }

    /// <summary>
    /// Shared appointment-creation core: artist validation, slot lock, conflict check, deposit
    /// calc, Appointment + BookingIntake + categorized Attachments construction, save, reminder
    /// scheduling, realtime notify, created-notification send. Called by both the authenticated
    /// handler above (after resolving clientId from the JWT/request) and
    /// CreateGuestAppointmentHandler (Public/Commands) after provisioning a new guest Client.
    /// Takes studioId explicitly rather than reading ICurrentTenant — the guest caller has no
    /// ambient tenant scope (no JWT) — same shape GetPublicStudioQuery and friends already use
    /// for every other public handler.
    /// </summary>
    internal static async Task<AppointmentResponse> CreateAppointmentCoreAsync(
        IAppDbContext db,
        Guid studioId,
        Guid clientId,
        CreateAppointmentRequest req,
        ISlotLocker slotLocker,
        IJobScheduler jobs,
        IRealtimeNotifier realtime,
        ISender sender,
        IPlanLimitService planLimits,
        CancellationToken ct)
    {
        DateTime requestEnd = req.Date.AddMinutes(req.DurationMinutes);

        Artist? artist = null;
        if (req.ArtistId is Guid artistId)
        {
            // IgnoreQueryFilters(): this core is shared with the anonymous guest-booking path,
            // which has no ambient tenant scope — see ArtistAvailabilityExtensions' doc comment
            // for why every query here must bypass the (Guid.Empty-scoped, for an anonymous
            // caller) global filter in favor of the explicit studioId predicate.
            artist = await db.Artists.IgnoreQueryFilters()
                .FirstOrDefaultAsync(a => a.StudioId == studioId && a.DeletedAt == null && a.Id == artistId, ct)
                ?? throw new NotFoundException(nameof(Artist), artistId);

            (bool available, string? reason) = await db.CheckArtistScheduleAsync(
                studioId, artistId, req.Date, req.DurationMinutes, ct);

            if (!available)
                throw new BusinessRuleViolationException(reason ?? "The artist is not available at that time.");
        }
        else
        {
            // ── Studio-choice path. Soft "someone can do this" check; no specific artist
            // resource is claimed here — the real per-artist claim happens in
            // AssignAppointmentArtistCommand. ──
            bool anyoneAvailable = await db.IsAnyArtistAvailableAsync(studioId, req.Date, req.DurationMinutes, ct);

            if (!anyoneAvailable)
                throw new BusinessRuleViolationException(
                    "No artist is available at that date and time. Please choose a different slot.");
        }

        bool locked = req.ArtistId is Guid lockArtistId
            && await slotLocker.TryAcquireLockAsync(studioId, lockArtistId, req.Date, ct);

        if (req.ArtistId is not null && !locked) throw new SlotAlreadyBookedException();

        try
        {
            if (req.ArtistId is Guid checkArtistId)
            {
                bool conflict = await db.Appointments.IgnoreQueryFilters().AnyAsync(a =>
                    a.StudioId == studioId &&
                    a.DeletedAt == null &&
                    a.ArtistId == checkArtistId &&
                    a.Date < requestEnd &&
                    a.EndDate > req.Date &&
                    a.Status != AppointmentStatus.Cancelled, ct);

                if (conflict) throw new SlotAlreadyBookedException();
            }

            // Single-active is enforced by the deposit rule handlers; ordering by
            // UpdatedAt keeps selection deterministic even against legacy data.
            DepositRule? rule = await db.DepositRules
                .IgnoreQueryFilters()
                .Where(r => r.StudioId == studioId && r.DeletedAt == null && r.IsActive)
                .OrderByDescending(r => r.UpdatedAt)
                .FirstOrDefaultAsync(ct);

            decimal depositAmount = DepositCalculator.Calculate(rule, artist?.HourlyRate, req.DurationMinutes);

            Appointment appointment = new()
            {
                StudioId = studioId,
                ArtistId = artist?.Id,
                ClientId = clientId,
                Date = req.Date,
                EndDate = requestEnd,
                DurationMinutes = req.DurationMinutes,
                Status = AppointmentStatus.Pending,
                DepositStatus = DepositStatus.Pending,
                DepositAmount = depositAmount,
                Notes = req.Notes
            };

            appointment.Intake = new BookingIntake
            {
                StudioId = studioId,
                TattooDescription = req.TattooDescription,
                SafetyNotes = req.SafetyNotes,
                DesiredPlacement = new BodyMap { Locations = req.DesiredPlacementLocations?.ToList() ?? [] },
                ReferralSource = req.ReferralSource is null
                    ? null
                    : Enum.Parse<ReferralSource>(req.ReferralSource),
                ReferralSourceOther = req.ReferralSourceOther,
            };

            foreach (AppointmentImageRequest image in req.Images ?? [])
            {
                appointment.Attachments.Add(new AppointmentAttachment
                {
                    StudioId = studioId,
                    ImageUrl = image.Url,
                    UploadedAt = DateTime.UtcNow,
                    Category = Enum.Parse<AppointmentAttachmentCategory>(image.Category),
                });
            }

            db.Appointments.Add(appointment);
            await db.SaveChangesAsync(ct);

            // Write-through cache invalidation — the next EnsureWithinLimitAsync call for
            // this studio reflects this new appointment immediately instead of up to 30s later.
            await planLimits.InvalidateUsageCacheAsync(QuotaType.AppointmentsPerMonth, ct);

            appointment.ReminderJobId48h = jobs.ScheduleAppointmentReminder(
                appointment.Id, "48h", appointment.Date.AddHours(-48));
            appointment.ReminderJobId24h = jobs.ScheduleAppointmentReminder(
                appointment.Id, "24h", appointment.Date.AddHours(-24));
            await db.SaveChangesAsync(ct);

            AppointmentResponse response = Map(appointment);
            await realtime.NotifyStudioAsync(studioId, "AppointmentCreated", response, ct);

            await sender.Send(new SendAppointmentCreatedNotificationCommand(appointment.Id), ct);

            return response;
        }
        finally
        {
            if (req.ArtistId is Guid unlockArtistId)
                await slotLocker.ReleaseLockAsync(studioId, unlockArtistId, req.Date, ct);
        }
    }

    internal static AppointmentResponse Map(
        Appointment a, string? clientName = null, string? artistName = null, Guid? clientUserId = null)
    {
        List<AppointmentAttachmentResponse> attachments = a.Attachments
            .OrderBy(x => x.UploadedAt)
            .Select(x => new AppointmentAttachmentResponse(x.ImageUrl, x.Category.ToString()))
            .ToList();

        return new(
            a.Id, a.StudioId, a.ArtistId, a.ClientId,
            a.Date, a.EndDate, a.DurationMinutes,
            a.Status.ToString(), a.DepositStatus.ToString(),
            a.DepositAmount, a.Notes, a.CreatedAt,
            a.CancellationReason?.ToString(),
            a.AftercareSentAt,
            clientName,
            // Deprecated flat mirror of the Reference-category subset — see AppointmentResponse.
            // Empty (not necessarily accurate) unless the caller eager-loaded
            // .Include(a => a.Attachments) — see GetAppointmentQuery.
            a.Attachments.Where(x => x.Category == AppointmentAttachmentCategory.Reference)
                .OrderBy(x => x.UploadedAt).Select(x => x.ImageUrl).ToList(),
            artistName,
            clientUserId,
            a.Intake?.TattooDescription,
            a.Intake?.SafetyNotes,
            a.Intake?.DesiredPlacement.Locations,
            a.Intake?.ReferralSource?.ToString(),
            a.Intake?.ReferralSourceOther,
            attachments);
    }
}
