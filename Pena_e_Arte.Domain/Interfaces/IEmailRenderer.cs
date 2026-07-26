namespace Pena_e_Arte.Domain.Interfaces;

public interface IEmailRenderer
{
    string RenderAppointmentConfirmation(
        string clientFirstName,
        DateTime date,
        int durationMinutes,
        string? notes,
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

    string RenderPasswordReset(string resetUrl);

    string RenderChangeEmailConfirmation(string confirmUrl);

    string RenderEmailChangedNotice(string newEmail);
}
