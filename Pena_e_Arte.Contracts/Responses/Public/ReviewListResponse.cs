namespace Pena_e_Arte.Contracts.Responses.Public;

/// <summary>
/// A public review list with its own summary. <see cref="Reviews"/> is the newest 50; the average and
/// the count cover every review of the target, so the header above the list never describes only the
/// page it happens to show. AverageRating is null when there are no reviews.
/// </summary>
public record ReviewListResponse(
    IReadOnlyList<ReviewResponse> Reviews,
    decimal? AverageRating,
    int ReviewCount);
