namespace Pena_e_Arte.Contracts.Requests;

public record AddStudioClosureRequest(DateTime StartDate, DateTime EndDate, string Reason);
