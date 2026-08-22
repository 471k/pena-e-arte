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

        DateTime requestEnd = req.Date.AddMinutes(req.DurationMinutes);

        Artist? artist = null;
        if (req.ArtistId is Guid artistId)
        {
            // ── Specific-artist path — UNCHANGED from today ──
            artist = await db.Artists.FirstOrDefaultAsync(a => a.Id == artistId, ct)
                ?? throw new NotFoundException(nameof(Artist), artistId);

            DayOfWeek requestDay = req.Date.DayOfWeek;
            TimeSpan requestStart = req.Date.TimeOfDay;
            TimeSpan requestEndTime = requestEnd.TimeOfDay;

            var scheduleEntry = await db.ArtistSchedules
                .Where(s => s.ArtistId == artistId && s.DayOfWeek == requestDay && s.IsAvailable)
                .FirstOrDefaultAsync(ct);

            if (scheduleEntry is null)
                throw new BusinessRuleViolationException($"The artist is not available on {requestDay}.");

            if (requestStart < scheduleEntry.StartTime || requestEndTime > scheduleEntry.EndTime)
                throw new BusinessRuleViolationException(
                    $"Appointment time is outside the artist's working hours ({scheduleEntry.StartTime:hh\\:mm}–{scheduleEntry.EndTime:hh\\:mm}).");

            bool onTimeOff = await db.ArtistTimeOffs.AnyAsync(
                t => t.ArtistId == artistId &&
                     t.StartDate <= req.Date.Date &&
                     t.EndDate >= req.Date.Date, ct);

            if (onTimeOff)
                throw new BusinessRuleViolationException("The artist is on leave on the requested date.");
        }
        else
        {
            // ── Studio-choice path — new. Soft "someone can do this" check; no specific artist
            // resource is claimed here (Decision #6) — the real per-artist claim happens in
            // AssignAppointmentArtistCommand. ──
            bool anyoneAvailable = await db.IsAnyArtistAvailableAsync(req.Date, req.DurationMinutes, ct);

            if (!anyoneAvailable)
                throw new BusinessRuleViolationException(
                    "No artist is available at that date and time. Please choose a different slot.");
        }

        bool locked = req.ArtistId is Guid lockArtistId
            && await slotLocker.TryAcquireLockAsync(tenant.StudioId, lockArtistId, req.Date, ct);

        if (req.ArtistId is not null && !locked) throw new SlotAlreadyBookedException();

        try
        {
            if (req.ArtistId is Guid checkArtistId)
            {
                bool conflict = await db.Appointments.AnyAsync(a =>
                    a.ArtistId == checkArtistId &&
                    a.Date < requestEnd &&
                    a.EndDate > req.Date &&
                    a.Status != AppointmentStatus.Cancelled, ct);

                if (conflict) throw new SlotAlreadyBookedException();
            }

            // Single-active is enforced by the deposit rule handlers; ordering by
            // UpdatedAt keeps selection deterministic even against legacy data.
            DepositRule? rule = await db.DepositRules
                .Where(r => r.IsActive)
                .OrderByDescending(r => r.UpdatedAt)
                .FirstOrDefaultAsync(ct);

            decimal depositAmount = DepositCalculator.Calculate(rule, artist?.HourlyRate, req.DurationMinutes);

            Appointment appointment = new()
            {
                StudioId = tenant.StudioId,
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

            foreach (string imageUrl in req.ImageUrls ?? [])
            {
                appointment.Attachments.Add(new AppointmentAttachment
                {
                    StudioId = tenant.StudioId,
                    ImageUrl = imageUrl,
                    UploadedAt = DateTime.UtcNow
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
            await realtime.NotifyStudioAsync(tenant.StudioId, "AppointmentCreated", response, ct);

            await sender.Send(new SendAppointmentCreatedNotificationCommand(appointment.Id), ct);

            return response;
        }
        finally
        {
            if (req.ArtistId is Guid unlockArtistId)
                await slotLocker.ReleaseLockAsync(tenant.StudioId, unlockArtistId, req.Date, ct);
        }
    }

    internal static AppointmentResponse Map(Appointment a, string? clientName = null, string? artistName = null) => new(
        a.Id, a.StudioId, a.ArtistId, a.ClientId,
        a.Date, a.EndDate, a.DurationMinutes,
        a.Status.ToString(), a.DepositStatus.ToString(),
        a.DepositAmount, a.Notes, a.CreatedAt,
        a.CancellationReason?.ToString(),
        a.AftercareSentAt,
        clientName,
        // Empty (not necessarily accurate) unless the caller eager-loaded
        // .Include(a => a.Attachments) — see GetAppointmentQuery.
        a.Attachments.OrderBy(x => x.UploadedAt).Select(x => x.ImageUrl).ToList(),
        artistName);
}
