namespace Pena_e_Arte.Domain.Interfaces;

public interface IEmailRenderer
{
    string RenderAppointmentConfirmation(
        string clientFirstName,
        DateTime date,
        int durationMinutes,
        string? notes,
        bool showBranding);

    string RenderAppointmentArtistAssigned(
        string clientFirstName,
        string artistFullName,
        DateTime date,
        string studioName,
        bool showBranding);

    string RenderAppointmentAssignedToArtist(
        string artistFirstName,
        string clientFullName,
        DateTime date,
        int durationMinutes,
        string? notes);

    string RenderAppointmentCreatedClient(
        string clientFirstName,
        DateTime date,
        int durationMinutes,
        string studioName,
        bool showBranding);

    string RenderAppointmentCreatedStudio(
        string clientFullName,
        DateTime date,
        int durationMinutes,
        string? notes);

    string RenderDesignApproved(
        string artistFirstName,
        string designTitle,
        string? clientNotes,
        bool showBranding);

    string RenderDesignChangesRequested(
        string artistFirstName,
        string designTitle,
        string? clientNotes,
        bool showBranding);

    string RenderIntakeFormSubmitted(
        string studioName,
        string clientFullName,
        string appointmentDate,
        bool showBranding);

    string RenderConsentFormSigned(
        string studioName,
        string clientFullName,
        string appointmentDate,
        bool showBranding);

    string RenderDepositCaptured(
        string clientFirstName,
        string amountFormatted,
        string appointmentDate,
        bool showBranding);

    string RenderPaymentRefunded(
        string clientFirstName,
        string amountFormatted,
        bool showBranding);

    string RenderAftercare(
        string clientFirstName,
        string studioName,
        string artistName,
        bool showBranding);

    string RenderEmailVerification(string confirmationUrl);

    /// <summary>
    /// isRejoiningArtist: true when the recipient already has working login credentials (a
    /// previously-removed artist reused for a new studio) — the copy tells them they can sign
    /// in immediately, offering the link only as an optional password reset, rather than
    /// implying (as for a brand-new account) that setting a password is required first.
    /// </summary>
    string RenderArtistInvite(string artistFirstName, string studioName, string setPasswordUrl, bool isRejoiningArtist = false);

    string RenderStudioJoinInvite(string studioName, string city, string manageInvitesUrl);

    string RenderPasswordReset(string resetUrl);

    /// <summary>
    /// Sent once, immediately after a guest checkout booking, carrying BOTH a password-reset
    /// link (Decision #2 — the guest's passwordless first booking; also doubles as their
    /// account-recovery safety net if this email is delayed/lost) and the standard
    /// email-confirmation link.
    /// </summary>
    string RenderGuestBookingWelcome(string studioName, string setPasswordUrl, string confirmEmailUrl);

    /// <summary>
    /// Sent instead of <see cref="RenderGuestBookingWelcome"/> when a guest-checkout email
    /// collides with an existing platform account — the sole disambiguation channel now that
    /// the HTTP response is identical either way (enumeration-resistance fix, 2026-09-01).
    /// </summary>
    string RenderGuestBookingEmailCollision(string studioName);

    string RenderChangeEmailConfirmation(string confirmUrl);

    string RenderEmailChangedNotice(string newEmail);

    /// <summary>
    /// Sent to every admin account when a new studio registers — never gated by
    /// INotificationPreferenceService (platform-ops notice, not studio-facing).
    /// </summary>
    string RenderStudioRegisteredAdmin(
        string studioName,
        string city,
        string ownerEmail,
        string? nipt,
        DateTime trialExpiresAtUtc,
        bool hasReferral,
        string studioDetailUrl);
}
