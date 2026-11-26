using System.Security.Cryptography;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Application.BookingDrafts;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Waitlists.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Public.Commands;

public record CreateGuestAppointmentCommand(string StudioSlug, CreateGuestAppointmentRequest Request)
    : IRequest<GuestBookingAckResponse>, IQuotaCheckedCommand
{
    // Guest bookings count toward the same plan quota as any other — do not give guests an
    // unmetered path around AppointmentsPerMonth.
    public QuotaType QuotaType => QuotaType.AppointmentsPerMonth;
}

public class CreateGuestAppointmentHandler(
    IAppDbContext db,
    ICurrentTenant tenant,
    IIdentityService identity,
    ISlotLocker slotLocker,
    IJobScheduler jobs,
    IRealtimeNotifier realtime,
    ISender sender,
    IPlanLimitService planLimits,
    IEmailRenderer emailRenderer,
    INotificationService notifications,
    IAppSettings appSettings,
    IBookingDraftResumeTokens resumeTokens,
    TimeProvider time,
    ILogger<CreateGuestAppointmentHandler> logger)
    : IRequestHandler<CreateGuestAppointmentCommand, GuestBookingAckResponse>
{
    private static readonly GuestBookingAckResponse Ack =
        new("Thanks — check your email to continue.");

    public async Task<GuestBookingAckResponse> Handle(CreateGuestAppointmentCommand command, CancellationToken ct)
    {
        CreateGuestAppointmentRequest req = command.Request;

        // Approved: public/anonymous studio-slug resolution (architecture.md AllowAnonymous
        // Exceptions / IgnoreQueryFilters) — shared with GetPublicStudioHandler and the other
        // guest-checkout handlers via PublicStudioLookupExtensions.
        Studio studio = await db.GetPublishedStudioBySlugAsync(command.StudioSlug, ct)
            ?? throw new NotFoundException(nameof(Studio), command.StudioSlug);

        // Scope the rest of this request to the studio being booked. The request has no tenant
        // JWT, so the global query filter would otherwise hide every tenant row from the
        // downstream handlers that look the new appointment up by id. Without this,
        // SendAppointmentCreatedNotificationHandler logged "Appointment … not found" and the
        // owner, the artist and the client got no booking-request notification for any guest
        // booking (found in the C-11 production test, 27 Oct 2026). Set unconditionally: the
        // endpoint is anonymous, so any tenant from a caller's own JWT is not the studio this
        // booking belongs to.
        tenant.SetTenant(studio.Id);

        // The generic PlanLimitBehavior pipeline check (this command's own IQuotaCheckedCommand)
        // is a silent no-op here: it resolves the plan via ICurrentTenant.StudioId, which stays
        // Guid.Empty for an anonymous request (no JWT) — no subscription is ever found for that
        // id, so EnsureWithinLimitAsync's early "no resolvable plan" return fires every time,
        // regardless of the real studio's actual usage. The explicit-studioId overload exists
        // precisely for this "caller knows the target studio isn't the ambient tenant" case (see
        // PlanLimitService.GetCurrentUsageAsync's own doc comment) — call it directly, now that
        // the real studio is resolved, before any writes (Identity user included) happen. Found
        // via /code-review, 2026-09-01.
        await planLimits.EnsureWithinLimitAsync(studio.Id, QuotaType.AppointmentsPerMonth, ct);

        // Duplicate-email handling (Decision #3), revised 2026-09-01 per /code-review finding:
        // the original design rejected a colliding email with a distinct 409
        // AccountAlreadyExistsException — correct in spirit (never silently attach a booking to
        // an account the caller hasn't proven control of) but an account-enumeration oracle in
        // practice, since the response shape alone told an anonymous caller whether any given
        // email has a platform account. Mirrors ForgotPasswordHandler's existing pattern
        // instead: respond identically either way; disambiguate only out-of-band, via email.
        // Residual, accepted risk: the two branches below still differ in response TIMING (the
        // real-booking path does materially more I/O — password hashing, multiple SaveChanges —
        // than the collision path's single lookup + fire-and-forget email), a side-channel this
        // fix does not close. Closing it would need artificial constant-time delays, judged not
        // worth the added latency for every real guest booking given the primary vector (a
        // direct, zero-effort response-shape check) is what this fix actually closes.
        Guid? existingUserId = await identity.GetUserIdByEmailAsync(req.Email, ct);
        if (existingUserId is not null)
        {
            // C05: keep what they typed as an UNCLAIMED draft and put a resume link in the notice. A failure here
            // must not change what the caller sees, exactly like the email failure below: the notice then goes out
            // without a link. Never log the email address.
            string? resumeUrl = null;
            try
            {
                resumeUrl = await SaveUnclaimedDraftAsync(studio, existingUserId.Value, req.Booking, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to save the unclaimed booking draft on the guest-booking email-collision path");
            }

            try
            {
                string body = emailRenderer.RenderGuestBookingEmailCollision(studio.Name, resumeUrl);
                await notifications.SendEmailAsync(
                    req.Email, $"Booking attempt at {studio.Name}", body, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send guest-booking email-collision notice");
            }

            return Ack;
        }

        // Approved: anonymous booking — cross-tenant-shaped Client lookup-by-email, identical
        // pattern to RegisterUserHandler's studio-pre-created-Client linking (approved exception
        // #28). IgnoreQueryFilters is required because this call has no tenant JWT.
        Client? client = await db.Clients
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.StudioId == studio.Id && c.Email == req.Email && c.UserId == null, ct);
        bool isNewClient = client is null;

        string randomPassword = GenerateRandomPassword();

        (bool success, Guid userId, string[] errors) = await identity.CreateUserAsync(
            req.Email, randomPassword, "client", studio.Id, req.FirstName);

        if (!success)
        {
            // TOCTOU: the GetUserIdByEmailAsync check above and this CreateUserAsync call are
            // not atomic — two concurrent guest submissions with the same brand-new email can
            // both pass the check before either commits. This does NOT create two accounts
            // sharing an email (IdentityService.CreateUserAsync sets UserName = email, and
            // AspNetUsers.UserNameIndex is a real unique DB index — the loser's CreateAsync call
            // fails at the database), but without this branch the loser previously surfaced a
            // raw BusinessRuleViolationException instead of degrading into the same graceful,
            // enumeration-resistant path as an up-front collision. Found via /code-review,
            // 2026-09-01.
            bool usernameTaken = errors.Any(e => e.Contains("already taken", StringComparison.OrdinalIgnoreCase));
            if (usernameTaken)
            {
                try
                {
                    string collisionBody = emailRenderer.RenderGuestBookingEmailCollision(studio.Name);
                    await notifications.SendEmailAsync(
                        req.Email, $"Booking attempt at {studio.Name}", collisionBody, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to send guest-booking email-collision notice (race path)");
                }

                return Ack;
            }

            throw new BusinessRuleViolationException(string.Join("; ", errors));
        }

        // Never log or return the random password — it exists only to satisfy Identity's
        // CreateUserAsync API; the guest sets their own via the reset-password link below.
        randomPassword = string.Empty;

        if (client is not null)
        {
            client.UserId = userId;
            client.FirstName = req.FirstName;
            client.LastName = req.LastName;
            client.Phone = req.Phone;
            client.MarketingOptIn = req.MarketingOptIn;
            client.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            client = new Client
            {
                StudioId = studio.Id,
                UserId = userId,
                FirstName = req.FirstName,
                LastName = req.LastName,
                Email = req.Email,
                Phone = req.Phone,
                MarketingOptIn = req.MarketingOptIn,
            };
            db.Clients.Add(client);
        }

        try
        {
            // CreateAppointmentCoreAsync's first SaveChangesAsync persists the Client row added/
            // modified above together with the Appointment + BookingIntake + Attachments in one
            // DB transaction (EF Core's default per-SaveChanges behavior) — the smallest
            // available atomicity guarantee given the Identity user already committed to its own
            // store above. If this throws, the Identity user is left orphaned (no linked Client)
            // — logged below for manual cleanup; residual risk accepted, see overnight prompt
            // Part 3c.
            await CreateAppointmentHandler.CreateAppointmentCoreAsync(
                db, studio.Id, client.Id, req.Booking, slotLocker, jobs, realtime, sender, planLimits, ct);

            if (isNewClient)
                jobs.EnqueueWebhookDelivery(studio.Id, "client.created", client.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "CreateGuestAppointmentHandler: booking failed after Identity user {@UserId} was already " +
                "created for studio {@StudioId} — orphaned login, needs manual cleanup", userId, studio.Id);
            throw;
        }

        // C09: a booking started from a waitlist claim closes that offer. Never fails the booking.
        if (!string.IsNullOrWhiteSpace(req.Booking.WaitlistClaimToken))
            await sender.Send(new CompleteWaitlistClaimCommand(studio.Id, client.Id, req.Booking.WaitlistClaimToken), ct);

        // Send email verification (non-blocking; failure must not abort the booking) — one
        // combined email carrying both the password-reset link (Decision #2's passwordless
        // first-booking flow) and the standard email-confirmation link.
        try
        {
            (bool resetSuccess, string? resetToken, _) = await identity.GeneratePasswordResetTokenAsync(req.Email);
            string confirmToken = await identity.GenerateEmailConfirmationTokenAsync(userId);

            string setPasswordUrl = resetSuccess && resetToken is not null
                ? $"{appSettings.BaseUrl}/reset-password?email={Uri.EscapeDataString(req.Email)}&token={Uri.EscapeDataString(resetToken)}"
                : $"{appSettings.BaseUrl}/forgot-password";
            string confirmEmailUrl =
                $"{appSettings.BaseUrl}/verify-email?token={Uri.EscapeDataString(confirmToken)}&userId={userId}";

            string body = emailRenderer.RenderGuestBookingWelcome(studio.Name, setPasswordUrl, confirmEmailUrl);
            await notifications.SendEmailAsync(req.Email, $"Set your password to manage your booking at {studio.Name}", body, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send guest-booking welcome email for user {@UserId}", userId);
        }

        return Ack;
    }

    // ASP.NET Core Identity policy (InfrastructureServiceExtensions): RequireDigit, RequiredLength=8,
    // RequireNonAlphanumeric=false, plus the framework defaults RequireUppercase/RequireLowercase=true.
    // 28 chars drawn from a mixed pool with one guaranteed char from each required class satisfies
    // this with wide margin — the exact policy may tighten over time, margin absorbs that.
    /// <summary>
    /// C05: saves what an anonymous caller typed as an UNCLAIMED draft for the account their email belongs to, and
    /// returns the resume link for the notice, or null when no link should be sent. The draft is stored now because
    /// the typed content has nowhere else to live until the link is followed; the token governs VISIBILITY, not
    /// storage. An unclaimed draft is invisible to its owner (GetMyBookingDraftQuery reads ClaimedAt != null), so
    /// nothing is attached to an account the caller has not proven control of: Decision #3 above still holds.
    ///
    /// Rulings (Desktop, C05 spec, 25 Nov 2026):
    /// - a CLAIMED draft at this studio is never overwritten: a stranger must not destroy work the owner saved
    ///   themselves. No token is minted and the notice goes out unchanged. An UNCLAIMED draft is replaced.
    /// - the safety notes are never stored: GuestBookingDraftPayload does not name them and BookingDraftRules
    ///   checks the result, the same exclusion every draft goes through.
    /// Also: only an account with the client role gets a draft. Drafts and the resume route are ClientOnly, so a
    /// staff account could never claim one and a stranger's text would sit for seven days with no use.
    /// </summary>
    private async Task<string?> SaveUnclaimedDraftAsync(
        Studio studio, Guid userId, CreateAppointmentRequest booking, CancellationToken ct)
    {
        IReadOnlyList<string> roles = await identity.GetUserRolesAsync(userId, ct);
        if (!roles.Contains("client", StringComparer.OrdinalIgnoreCase)) return null;

        string? payload = GuestBookingDraftPayload.From(booking, studio.Timezone);
        if (payload is null
            || !BookingDraftRules.IsJsonObject(payload)
            || BookingDraftRules.HoldsExcludedField(payload))
        {
            return null;
        }

        // Tenant-scoped by the global query filter (SetTenant above): this studio only.
        BookingDraft? existing = await db.BookingDrafts
            .Include(d => d.Attachments)
            .FirstOrDefaultAsync(d => d.UserId == userId, ct);
        if (existing is { ClaimedAt: not null }) return null;
        if (existing is not null)
        {
            // A new row, not an update, so a link sent for the replaced draft stops matching (new id).
            db.BookingDraftAttachments.RemoveRange(existing.Attachments);
            db.BookingDrafts.Remove(existing);
        }

        DateTime now = time.GetUtcNow().UtcDateTime;
        // Whole seconds: CreatedAt is carried in the token as ticks and compared after a database round trip.
        DateTime createdAt = new(now.Ticks - (now.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
        BookingDraft draft = new()
        {
            StudioId = studio.Id,
            UserId = userId,
            PayloadJson = payload,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            ExpiresAt = createdAt + BookingDraft.Lifetime,
            ClaimedAt = null,
        };
        foreach (AppointmentImageRequest image in booking.Images ?? [])
        {
            draft.Attachments.Add(new BookingDraftAttachment
            {
                StudioId = studio.Id,
                DraftId = draft.Id,
                ImageUrl = image.Url,
                Category = Enum.Parse<AppointmentAttachmentCategory>(image.Category),
            });
        }
        db.BookingDrafts.Add(draft);
        await db.SaveChangesAsync(ct);

        // The token goes in the URL FRAGMENT, which browsers never send to a server, so it stays out of Cloudflare,
        // ingress and request logs (the C09 rule). The studio slug is not a secret: the email names the studio.
        return $"{appSettings.BaseUrl}/booking-drafts/resume?studio={Uri.EscapeDataString(studio.Slug)}#t={resumeTokens.Mint(draft)}";
    }

    private static string GenerateRandomPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string symbols = "!@#$%^&*-_=+";
        const string all = upper + lower + digits + symbols;
        const int length = 28;

        char[] chars = new char[length];
        chars[0] = upper[RandomNumberGenerator.GetInt32(upper.Length)];
        chars[1] = lower[RandomNumberGenerator.GetInt32(lower.Length)];
        chars[2] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
        for (int i = 3; i < length; i++)
            chars[i] = all[RandomNumberGenerator.GetInt32(all.Length)];

        // Fisher-Yates shuffle so the guaranteed-class characters aren't always in positions 0-2.
        for (int i = chars.Length - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }
}
