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

    string RenderArtistInvite(string artistFirstName, string studioName, string setPasswordUrl);

    string RenderStudioJoinInvite(string studioName, string city, string manageInvitesUrl);

    string RenderPasswordReset(string resetUrl);

    /// <summary>
    /// Sent once, immediately after a guest checkout booking, carrying BOTH a password-reset
    /// link (Decision #2 — the guest's passwordless first booking; also doubles as their
    /// account-recovery safety net if this email is delayed/lost) and the standard
    /// email-confirmation link.
    /// </summary>
    string RenderGuestBookingWelcome(string studioName, string setPasswordUrl, string confirmEmailUrl);

    string RenderChangeEmailConfirmation(string confirmUrl);

    string RenderEmailChangedNotice(string newEmail);
}
