using System.Globalization;
using System.Reflection;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services.MailKit;

public class EmailRenderer : IEmailRenderer
{
    private static readonly string _confirmationTemplate =
        LoadEmbeddedTemplate("AppointmentConfirmation.html");

    private static readonly string _appointmentCreatedClientTemplate =
        LoadEmbeddedTemplate("AppointmentCreatedClient.html");

    private static readonly string _appointmentCreatedStudioTemplate =
        LoadEmbeddedTemplate("AppointmentCreatedStudio.html");

    private static readonly string _designApprovedTemplate =
        LoadEmbeddedTemplate("DesignApproved.html");

    private static readonly string _designChangesRequestedTemplate =
        LoadEmbeddedTemplate("DesignChangesRequested.html");

    private static readonly string _intakeFormSubmittedTemplate =
        LoadEmbeddedTemplate("IntakeFormSubmitted.html");

    private static readonly string _consentFormSignedTemplate =
        LoadEmbeddedTemplate("ConsentFormSigned.html");

    private static readonly string _depositCapturedTemplate =
        LoadEmbeddedTemplate("DepositCaptured.html");

    private static readonly string _paymentRefundedTemplate =
        LoadEmbeddedTemplate("PaymentRefunded.html");

    private static string LoadEmbeddedTemplate(string fileName)
    {
        Assembly assembly    = typeof(EmailRenderer).Assembly;
        string resourceName  = $"Pena_e_Arte.Infrastructure.Services.MailKit.Templates.{fileName}";
        using Stream stream  = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded email template '{resourceName}' not found in assembly '{assembly.FullName}'.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    public string RenderAppointmentConfirmation(
        string   clientFirstName,
        DateTime date,
        int      durationMinutes,
        string?  notes,
        bool     showBranding)
    {
        Dictionary<string, string> vars = new()
        {
            ["client_first_name"] = clientFirstName,
            ["appointment_date"]  = date.ToString("dddd, dd MMMM yyyy 'at' HH:mm"),
            ["duration_minutes"]  = durationMinutes.ToString(),
            ["notes"]             = notes ?? string.Empty,
            ["show_notes"]        = (notes is not null).ToString().ToLowerInvariant(),
            ["show_branding"]     = showBranding.ToString().ToLowerInvariant(),
        };

        return TemplateRenderer.Render(_confirmationTemplate, vars);
    }

    public string RenderAppointmentCreatedClient(
        string   clientFirstName,
        DateTime date,
        int      durationMinutes,
        string   studioName,
        bool     showBranding)
    {
        Dictionary<string, string> vars = new()
        {
            ["client_first_name"] = clientFirstName,
            ["appointment_date"]  = date.ToString("dddd, dd MMMM yyyy 'at' HH:mm", CultureInfo.InvariantCulture),
            ["duration_minutes"]  = durationMinutes.ToString(),
            ["studio_name"]       = studioName,
            ["show_branding"]     = showBranding.ToString().ToLowerInvariant(),
        };

        return TemplateRenderer.Render(_appointmentCreatedClientTemplate, vars);
    }

    public string RenderAppointmentCreatedStudio(
        string   clientFullName,
        DateTime date,
        int      durationMinutes,
        string?  notes)
    {
        Dictionary<string, string> vars = new()
        {
            ["client_full_name"]  = clientFullName,
            ["appointment_date"]  = date.ToString("dddd, dd MMMM yyyy 'at' HH:mm", CultureInfo.InvariantCulture),
            ["duration_minutes"]  = durationMinutes.ToString(),
            ["notes"]             = notes ?? string.Empty,
            ["show_notes"]        = (notes is not null).ToString().ToLowerInvariant(),
        };

        return TemplateRenderer.Render(_appointmentCreatedStudioTemplate, vars);
    }

    public string RenderDesignApproved(
        string  artistFirstName,
        string  designTitle,
        string? clientNotes,
        bool    showBranding)
    {
        Dictionary<string, string> vars = new()
        {
            ["artist_first_name"] = artistFirstName,
            ["design_title"]      = designTitle,
            ["client_notes"]      = clientNotes ?? string.Empty,
            ["show_branding"]     = showBranding.ToString().ToLowerInvariant(),
        };

        return TemplateRenderer.Render(_designApprovedTemplate, vars);
    }

    public string RenderDesignChangesRequested(
        string  artistFirstName,
        string  designTitle,
        string? clientNotes,
        bool    showBranding)
    {
        Dictionary<string, string> vars = new()
        {
            ["artist_first_name"] = artistFirstName,
            ["design_title"]      = designTitle,
            ["client_notes"]      = clientNotes ?? string.Empty,
            ["show_branding"]     = showBranding.ToString().ToLowerInvariant(),
        };

        return TemplateRenderer.Render(_designChangesRequestedTemplate, vars);
    }

    public string RenderIntakeFormSubmitted(
        string studioName,
        string clientFullName,
        string appointmentDate,
        bool   showBranding)
    {
        Dictionary<string, string> vars = new()
        {
            ["studio_name"]       = studioName,
            ["client_full_name"]  = clientFullName,
            ["appointment_date"]  = appointmentDate,
            ["show_branding"]     = showBranding.ToString().ToLowerInvariant(),
        };

        return TemplateRenderer.Render(_intakeFormSubmittedTemplate, vars);
    }

    public string RenderConsentFormSigned(
        string studioName,
        string clientFullName,
        string appointmentDate,
        bool   showBranding)
    {
        Dictionary<string, string> vars = new()
        {
            ["studio_name"]       = studioName,
            ["client_full_name"]  = clientFullName,
            ["appointment_date"]  = appointmentDate,
            ["show_branding"]     = showBranding.ToString().ToLowerInvariant(),
        };

        return TemplateRenderer.Render(_consentFormSignedTemplate, vars);
    }

    public string RenderDepositCaptured(
        string clientFirstName,
        string amountFormatted,
        string appointmentDate,
        bool   showBranding)
    {
        Dictionary<string, string> vars = new()
        {
            ["client_first_name"] = clientFirstName,
            ["amount_formatted"]  = amountFormatted,
            ["appointment_date"]  = appointmentDate,
            ["show_branding"]     = showBranding.ToString().ToLowerInvariant(),
        };

        return TemplateRenderer.Render(_depositCapturedTemplate, vars);
    }

    public string RenderPaymentRefunded(
        string clientFirstName,
        string amountFormatted,
        bool   showBranding)
    {
        Dictionary<string, string> vars = new()
        {
            ["client_first_name"] = clientFirstName,
            ["amount_formatted"]  = amountFormatted,
            ["show_branding"]     = showBranding.ToString().ToLowerInvariant(),
        };

        return TemplateRenderer.Render(_paymentRefundedTemplate, vars);
    }
}
