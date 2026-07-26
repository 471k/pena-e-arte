namespace Pena_e_Arte.Contracts.Responses;

public record HelpSearchInsightsResponse(
    int TotalSearches,
    int Days,
    List<HelpQueryFrequency> TopQueries,
    List<HelpQueryFrequency> ZeroResultQueries);

public record HelpQueryFrequency(
    string Query,
    int Count,
    string[] RolesAsked);
