namespace Pena_e_Arte.Contracts.Requests;

/// <summary>
/// Client self-service edit of their own contact details. Email is deliberately absent — it is the
/// login identity and changes only through the confirmed change-email flow. A null/blank
/// <paramref name="Phone"/> clears the number.
/// </summary>
public record UpdateMyClientRequest(
    string FirstName,
    string LastName,
    string? Phone);
