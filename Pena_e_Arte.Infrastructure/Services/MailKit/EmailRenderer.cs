using System.Reflection;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services.MailKit;

public class EmailRenderer : IEmailRenderer
{
    private static readonly string _confirmationTemplate =
        LoadEmbeddedTemplate("AppointmentConfirmation.html");

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
}
