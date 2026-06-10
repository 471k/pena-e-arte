namespace Pena_e_Arte.Domain.Interfaces;

public interface IEmailRenderer
{
    string RenderAppointmentConfirmation(
        string    clientFirstName,
        DateTime  date,
        int       durationMinutes,
        string?   notes,
        bool      showBranding);
}
