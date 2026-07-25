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

    public string RenderAftercare(
        string clientFirstName,
        string studioName,
        string artistName,
        bool   showBranding)
    {
        string branding = showBranding
            ? "<p style=\"text-align:center;font-size:12px;color:#9ca3af\">Powered by TattooOS</p>"
            : "";

        return $"""
            <!DOCTYPE html>
            <html>
            <head><meta charset="utf-8"><title>Tattoo Aftercare</title></head>
            <body style="font-family:sans-serif;max-width:600px;margin:0 auto;padding:24px">
              <h1 style="color:#7c3aed">Aftercare Instructions</h1>
              <p>Hi {System.Net.WebUtility.HtmlEncode(clientFirstName)},</p>
              <p>Thank you for your session with {System.Net.WebUtility.HtmlEncode(artistName)} at {System.Net.WebUtility.HtmlEncode(studioName)}!
              Here are your aftercare instructions to keep your new tattoo looking great:</p>
              <ul>
                <li>Keep the tattoo covered for 2–4 hours after the session.</li>
                <li>Gently wash with lukewarm water and fragrance-free soap.</li>
                <li>Pat dry — never rub.</li>
                <li>Apply a thin layer of unscented moisturizer 2–3 times daily for 2 weeks.</li>
                <li>Avoid sun exposure, pools, and soaking for 2 weeks.</li>
                <li>Contact the studio if you notice redness, swelling, or discharge after 3 days.</li>
              </ul>
              <p>We hope to see you again soon!</p>
              {branding}
            </body>
            </html>
            """;
    }

    public string RenderEmailVerification(string confirmationUrl) =>
        $"""
        <!DOCTYPE html>
        <html>
        <head><meta charset="utf-8"><title>Confirm your email</title></head>
        <body style="font-family:sans-serif;max-width:600px;margin:0 auto;padding:24px">
          <h1 style="color:#7c3aed">Confirm your TattooOS account</h1>
          <p>Click the button below to verify your email address:</p>
          <a href="{System.Net.WebUtility.HtmlEncode(confirmationUrl)}"
             style="display:inline-block;background:#7c3aed;color:#fff;padding:12px 24px;border-radius:6px;text-decoration:none">
            Confirm Email
          </a>
          <p style="color:#6b7280;font-size:12px;margin-top:24px">
            If you did not create an account, you can ignore this email.
          </p>
        </body>
        </html>
        """;

    public string RenderArtistInvite(string artistFirstName, string studioName, string setPasswordUrl) =>
        $"""
        <!DOCTYPE html>
        <html>
        <head><meta charset="utf-8"><title>You've been invited to {System.Net.WebUtility.HtmlEncode(studioName)}</title></head>
        <body style="font-family:sans-serif;max-width:600px;margin:0 auto;padding:24px">
          <h1 style="color:#7c3aed">Welcome to TattooOS</h1>
          <p>Hi {System.Net.WebUtility.HtmlEncode(artistFirstName)},</p>
          <p>You've been added as an artist at <strong>{System.Net.WebUtility.HtmlEncode(studioName)}</strong>.
          Click the button below to set your password and activate your account.</p>
          <a href="{System.Net.WebUtility.HtmlEncode(setPasswordUrl)}"
             style="display:inline-block;background:#7c3aed;color:#fff;padding:12px 24px;border-radius:6px;text-decoration:none;margin:16px 0">
            Set your password
          </a>
          <p style="color:#6b7280;font-size:12px;margin-top:24px">
            This link expires in 1 hour. If you were not expecting this invitation, you can ignore this email.
          </p>
        </body>
        </html>
        """;

    public string RenderPasswordReset(string resetUrl) =>
        $"""
        <!DOCTYPE html>
        <html>
        <head><meta charset="utf-8"><title>Reset your password</title></head>
        <body style="font-family:sans-serif;max-width:600px;margin:0 auto;padding:24px">
          <h1 style="color:#7c3aed">Reset your password</h1>
          <p>We received a request to reset your TattooOS password. Click the button below to choose a new one.</p>
          <a href="{System.Net.WebUtility.HtmlEncode(resetUrl)}"
             style="display:inline-block;background:#7c3aed;color:#fff;padding:12px 24px;border-radius:6px;text-decoration:none;margin:16px 0">
            Reset password
          </a>
          <p style="color:#6b7280;font-size:12px;margin-top:24px">
            This link expires in 1 hour. If you did not request a password reset, you can safely ignore this email.
          </p>
        </body>
        </html>
        """;
}
