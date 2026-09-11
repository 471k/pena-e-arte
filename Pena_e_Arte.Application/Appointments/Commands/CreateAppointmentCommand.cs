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

            // Package-covered booking: skip DepositCalculator entirely (already paid for via the
            // package purchase) rather than computing a deposit that will just be zeroed out.
            PackagePurchase? packagePurchase = null;
            decimal depositAmount;
            DepositStatus depositStatus;

            if (req.PackagePurchaseId is Guid packagePurchaseId)
            {
                packagePurchase = await db.PackagePurchases.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(p => p.StudioId == studioId && p.DeletedAt == null && p.Id == packagePurchaseId, ct)
                    ?? throw new NotFoundException(nameof(PackagePurchase), packagePurchaseId);

                if (packagePurchase.ClientId != clientId)
                    throw new NotFoundException(nameof(PackagePurchase), packagePurchaseId);

                if (packagePurchase.SessionsRemaining <= 0)
                    throw new BusinessRuleViolationException("This package has no sessions remaining.");

                depositAmount = 0;
                depositStatus = DepositStatus.PrePaid;
            }
            else
            {
                // Single-active is enforced by the deposit rule handlers; ordering by
                // UpdatedAt keeps selection deterministic even against legacy data.
                DepositRule? rule = await db.DepositRules
                    .IgnoreQueryFilters()
                    .Where(r => r.StudioId == studioId && r.DeletedAt == null && r.IsActive)
                    .OrderByDescending(r => r.UpdatedAt)
                    .FirstOrDefaultAsync(ct);

                depositAmount = DepositCalculator.Calculate(rule, artist?.HourlyRate, req.DurationMinutes);
                depositStatus = DepositStatus.Pending;
            }

            // ── Discount stacking order (documented once, here, rather than scattered across
            // uncoordinated edits): promo code → gift card → referral-code redemption →
            // referral-reward redemption, each applied to whatever remains after the previous
            // one, floored at 0. Gift card (P1 Group 3) redemption at booking time doesn't
            // exist — gift cards are redeemed separately, not wired into this handler. Entire
            // block skipped for a package-covered booking: depositAmount is already 0 (paid for
            // via the package purchase), so applying a discount would do nothing except
            // needlessly consume the client's code/reward for zero benefit.
            // IgnoreQueryFilters() throughout this block for the same reason as
            // Artists/DepositRules above — this core is shared with the anonymous
            // guest-booking path, which has no ambient tenant scope.
            bool promoCodeApplied = false;
            ClientReferralCode? redeemedReferralCode = null;
            ClientReferralReward? spentReward = null;

            if (packagePurchase is null)
            {
                if (!string.IsNullOrWhiteSpace(req.PromoCode))
                {
                    string normalizedCode = req.PromoCode.Trim().ToUpperInvariant();
                    DateTime now = DateTime.UtcNow;

                    PromoCode? promoCode = await db.PromoCodes
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(p =>
                            p.StudioId == studioId &&
                            p.DeletedAt == null &&
                            p.IsActive &&
                            p.Code == normalizedCode &&
                            (p.ExpiresAt == null || p.ExpiresAt > now) &&
                            (p.MaxRedemptions == null || p.RedemptionCount < p.MaxRedemptions.Value), ct);

                    // A guest fat-fingering a promo code should never block their booking — a
                    // missing/expired/exhausted/wrong-studio code is silently ignored rather than
                    // thrown; PromoCodeApplied on the response tells the frontend whether to show
                    // a "not recognized" note.
                    if (promoCode is not null)
                    {
                        decimal discount = promoCode.AmountFixed
                            ?? Math.Round(depositAmount * (promoCode.AmountPercent ?? 0m) / 100m, 2, MidpointRounding.AwayFromZero);

                        depositAmount = Math.Max(0m, depositAmount - discount);
                        promoCode.RedemptionCount++;
                        promoCode.UpdatedAt = now;
                        promoCodeApplied = true;
                    }
                }

                if (!string.IsNullOrWhiteSpace(req.ReferralCode))
                {
                    redeemedReferralCode = await db.ClientReferralCodes.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(c => c.StudioId == studioId && c.DeletedAt == null && c.Code == req.ReferralCode, ct)
                        ?? throw new BusinessRuleViolationException("That referral code isn't valid for this studio.");

                    if (redeemedReferralCode.ReferrerClientId == clientId)
                        throw new BusinessRuleViolationException("You can't redeem your own referral code.");

                    bool alreadyRedeemed = await db.ClientReferralRedemptions.IgnoreQueryFilters().AnyAsync(r =>
                        r.ClientReferralCodeId == redeemedReferralCode.Id && r.RedeemedByClientId == clientId, ct);
                    if (alreadyRedeemed)
                        throw new BusinessRuleViolationException("You've already redeemed this referral code.");

                    depositAmount = Math.Max(0, depositAmount - depositAmount * redeemedReferralCode.RewardPercent / 100m);
                }

                if (req.ReferralRewardId is Guid rewardId)
                {
                    spentReward = await db.ClientReferralRewards.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(r => r.Id == rewardId && r.DeletedAt == null, ct)
                        ?? throw new NotFoundException(nameof(ClientReferralReward), rewardId);

                    if (spentReward.ClientId != clientId)
                        throw new BusinessRuleViolationException("This referral reward doesn't belong to you.");
                    if (spentReward.IsRedeemed)
                        throw new BusinessRuleViolationException("This referral reward has already been redeemed.");

                    depositAmount = Math.Max(0, depositAmount - depositAmount * spentReward.RewardPercent / 100m);
                }
            }

            Appointment appointment = new()
            {
                StudioId = studioId,
                ArtistId = artist?.Id,
                ClientId = clientId,
                Date = req.Date,
                EndDate = requestEnd,
                DurationMinutes = req.DurationMinutes,
                Status = AppointmentStatus.Pending,
                DepositStatus = depositStatus,
                DepositAmount = depositAmount,
                Notes = req.Notes
            };

            appointment.Intake = new BookingIntake
            {
                StudioId = studioId,
                TattooDescription = req.TattooDescription,
                Style = req.Style,
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

            if (packagePurchase is not null)
            {
                packagePurchase.SessionsRemaining--;
                packagePurchase.UpdatedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(ct);

            // Write-through cache invalidation — the next EnsureWithinLimitAsync call for
            // this studio reflects this new appointment immediately instead of up to 30s later.
            await planLimits.InvalidateUsageCacheAsync(QuotaType.AppointmentsPerMonth, ct);

            appointment.ReminderJobId48h = jobs.ScheduleAppointmentReminder(
                appointment.Id, "48h", appointment.Date.AddHours(-48));
            appointment.ReminderJobId24h = jobs.ScheduleAppointmentReminder(
                appointment.Id, "24h", appointment.Date.AddHours(-24));

            if (redeemedReferralCode is not null)
            {
                // Ids are client-generated (TenantEntity.Id = Guid.NewGuid() at construction),
                // not DB-assigned — safe to reference redemption.Id before SaveChangesAsync.
                ClientReferralRedemption redemption = new()
                {
                    StudioId = studioId,
                    ClientReferralCodeId = redeemedReferralCode.Id,
                    RedeemedByClientId = clientId,
                    AppointmentId = appointment.Id,
                };
                db.ClientReferralRedemptions.Add(redemption);
                redeemedReferralCode.RedemptionCount++;
                redeemedReferralCode.UpdatedAt = DateTime.UtcNow;

                // Two-sided: the referrer doesn't get their discount applied to anything right
                // now (they aren't necessarily booking) — they get a spendable credit instead.
                db.ClientReferralRewards.Add(new ClientReferralReward
                {
                    StudioId = studioId,
                    ClientId = redeemedReferralCode.ReferrerClientId,
                    SourceRedemptionId = redemption.Id,
                    RewardPercent = redeemedReferralCode.RewardPercent,
                    IsRedeemed = false,
                });
            }

            if (spentReward is not null)
            {
                spentReward.IsRedeemed = true;
                spentReward.RedeemedOnAppointmentId = appointment.Id;
                spentReward.UpdatedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(ct);

            AppointmentResponse response = Map(appointment, promoCodeApplied: promoCodeApplied);
            await realtime.NotifyStudioAsync(studioId, "AppointmentCreated", response, ct);

            await sender.Send(new SendAppointmentCreatedNotificationCommand(appointment.Id), ct);
            jobs.EnqueueWebhookDelivery(studioId, "appointment.created", appointment.Id);

            return response;
        }
        finally
        {
            if (req.ArtistId is Guid unlockArtistId)
                await slotLocker.ReleaseLockAsync(studioId, unlockArtistId, req.Date, ct);
        }
    }

    internal static AppointmentResponse Map(
        Appointment a, string? clientName = null, string? artistName = null, Guid? clientUserId = null,
        bool promoCodeApplied = false)
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
            a.Intake?.Style,
            a.Intake?.SafetyNotes,
            a.Intake?.DesiredPlacement.Locations,
            a.Intake?.ReferralSource?.ToString(),
            a.Intake?.ReferralSourceOther,
            attachments,
            promoCodeApplied);
    }
}
