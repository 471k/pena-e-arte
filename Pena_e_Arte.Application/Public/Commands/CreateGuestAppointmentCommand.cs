using System.Security.Cryptography;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Public.Commands;

public record CreateGuestAppointmentCommand(string StudioSlug, CreateGuestAppointmentRequest Request)
    : IRequest<AppointmentResponse>, IQuotaCheckedCommand
{
    // Guest bookings count toward the same plan quota as any other — do not give guests an
    // unmetered path around AppointmentsPerMonth.
    public QuotaType QuotaType => QuotaType.AppointmentsPerMonth;
}

public class CreateGuestAppointmentHandler(
    IAppDbContext db,
    IIdentityService identity,
    ISlotLocker slotLocker,
    IJobScheduler jobs,
    IRealtimeNotifier realtime,
    ISender sender,
    IPlanLimitService planLimits,
    IEmailRenderer emailRenderer,
    INotificationService notifications,
    IAppSettings appSettings,
    ILogger<CreateGuestAppointmentHandler> logger)
    : IRequestHandler<CreateGuestAppointmentCommand, AppointmentResponse>
{
    public async Task<AppointmentResponse> Handle(CreateGuestAppointmentCommand command, CancellationToken ct)
    {
        CreateGuestAppointmentRequest req = command.Request;

        // Approved: public/anonymous studio-slug resolution — same predicate as
        // GetPublicStudioHandler (architecture.md AllowAnonymous Exceptions / IgnoreQueryFilters).
        Studio studio = await db.Studios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Slug == command.StudioSlug && s.IsActive && s.IsPublished, ct)
            ?? throw new NotFoundException(nameof(Studio), command.StudioSlug);

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

        // Duplicate-email handling (Decision #3): an anonymous caller must never be allowed to
        // attach a booking (and medical intake data) to an existing account without proving
        // control of it. Identity users are platform-global, so this check is not studio-scoped.
        Guid? existingUserId = await identity.GetUserIdByEmailAsync(req.Email, ct);
        if (existingUserId is not null)
            throw new AccountAlreadyExistsException();

        // Approved: anonymous booking — cross-tenant-shaped Client lookup-by-email, identical
        // pattern to RegisterUserHandler's studio-pre-created-Client linking (approved exception
        // #28). IgnoreQueryFilters is required because this call has no tenant JWT.
        Client? client = await db.Clients
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.StudioId == studio.Id && c.Email == req.Email && c.UserId == null, ct);

        string randomPassword = GenerateRandomPassword();

        (bool success, Guid userId, string[] errors) = await identity.CreateUserAsync(
            req.Email, randomPassword, "client", studio.Id, req.FirstName);

        if (!success)
            throw new BusinessRuleViolationException(string.Join("; ", errors));

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

        AppointmentResponse response;
        try
        {
            // CreateAppointmentCoreAsync's first SaveChangesAsync persists the Client row added/
            // modified above together with the Appointment + BookingIntake + Attachments in one
            // DB transaction (EF Core's default per-SaveChanges behavior) — the smallest
            // available atomicity guarantee given the Identity user already committed to its own
            // store above. If this throws, the Identity user is left orphaned (no linked Client)
            // — logged below for manual cleanup; residual risk accepted, see overnight prompt
            // Part 3c.
            response = await CreateAppointmentHandler.CreateAppointmentCoreAsync(
                db, studio.Id, client.Id, req.Booking, slotLocker, jobs, realtime, sender, planLimits, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "CreateGuestAppointmentHandler: booking failed after Identity user {@UserId} was already " +
                "created for studio {@StudioId} — orphaned login, needs manual cleanup", userId, studio.Id);
            throw;
        }

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
            await notifications.SendEmailAsync(req.Email, $"Your booking at {studio.Name} is confirmed", body, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send guest-booking welcome email for user {@UserId}", userId);
        }

        return response;
    }

    // ASP.NET Core Identity policy (InfrastructureServiceExtensions): RequireDigit, RequiredLength=8,
    // RequireNonAlphanumeric=false, plus the framework defaults RequireUppercase/RequireLowercase=true.
    // 28 chars drawn from a mixed pool with one guaranteed char from each required class satisfies
    // this with wide margin — the exact policy may tighten over time, margin absorbs that.
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
