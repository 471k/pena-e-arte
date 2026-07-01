namespace Pena_e_Arte.Contracts.Requests;

public record AddArtistTimeOffRequest(DateTime StartDate, DateTime EndDate, string Reason);
