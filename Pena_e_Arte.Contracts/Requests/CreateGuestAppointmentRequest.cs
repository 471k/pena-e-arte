namespace Pena_e_Arte.Contracts.Requests;

public record CreateGuestAppointmentRequest(
    string FirstName,
    string LastName,
    string Email,
    string Phone,               // E.164, from PhoneInput — same shape CreateClientRequest already requires
    bool MarketingOptIn,
    CreateAppointmentRequest Booking);   // reuses every booking-content field; Booking.ClientId is ignored
