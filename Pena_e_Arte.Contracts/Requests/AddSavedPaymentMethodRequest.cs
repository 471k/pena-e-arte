namespace Pena_e_Arte.Contracts.Requests;

/// <summary>Body sent from AddCardForm.onSuccess — never a PAN, only the single-use JWE and
/// (POK's widget also returns this alongside it) security code, plus the billing fields the
/// widget collected. Every field here is one AddCardData actually contains.</summary>
public record AddSavedPaymentMethodRequest(
    string Jwe,
    string? SecurityCode,
    string FirstName,
    string LastName,
    string Email,
    string CountryCode,
    string? AdministrativeArea,
    string? Locality,
    string? Address1,
    string? PostalCode,
    string? PhoneNumber);
