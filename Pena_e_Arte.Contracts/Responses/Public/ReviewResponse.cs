namespace Pena_e_Arte.Contracts.Responses.Public;

public record ReviewResponse(
    Guid     Id,
    string   AuthorName,
    int      Rating,
    string   Body,
    DateTime CreatedAt);
