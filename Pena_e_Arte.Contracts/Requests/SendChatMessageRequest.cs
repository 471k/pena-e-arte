namespace Pena_e_Arte.Contracts.Requests;

/// <param name="AppointmentId">Optional: the appointment the message is about. It must belong to the
/// client in this conversation.</param>
public record SendChatMessageRequest(string Body, Guid? AppointmentId = null);
