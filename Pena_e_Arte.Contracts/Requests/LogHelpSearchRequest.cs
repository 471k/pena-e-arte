namespace Pena_e_Arte.Contracts.Requests;

public record LogHelpSearchRequest(
    string Query,
    int ResultCount);
