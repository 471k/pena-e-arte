namespace Pena_e_Arte.Contracts.Requests;

public record UpdateFeedbackStatusRequest(
    string Status,
    string? AdminNote);
