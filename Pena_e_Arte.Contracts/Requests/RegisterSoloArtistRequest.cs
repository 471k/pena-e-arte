namespace Pena_e_Arte.Contracts.Requests;

public record RegisterSoloArtistRequest(
    string Email,
    string Password,
    string FirstName,
    string LastName);
