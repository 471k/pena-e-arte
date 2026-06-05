namespace Pena_e_Arte.Contracts.Requests;

public record UpdateStudioRequest(
    string Name,
    string City,
    double Latitude,
    double Longitude);
