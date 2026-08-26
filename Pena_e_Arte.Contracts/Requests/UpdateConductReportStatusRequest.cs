namespace Pena_e_Arte.Contracts.Requests;

public record UpdateConductReportStatusRequest(string Status, string? ResolutionNote = null);
