namespace Pena_e_Arte.Contracts.Requests;

public record RescheduleAppointmentRequest(DateTime NewDate, int NewDurationMinutes, string? Notes);
