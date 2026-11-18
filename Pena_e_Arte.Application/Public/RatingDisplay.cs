namespace Pena_e_Arte.Application.Public;

/// <summary>
/// The one rule for the star-rating average the product shows (studio, artist and tattoo pages, the
/// discovery map and the feed): the mean of the whole-star ratings to one decimal, a half rounded up
/// (4.25 shows as 4.3, 4.35 as 4.4), and no number at all when there are no reviews.
///
/// The mean is taken from the integer sum and count in <see cref="decimal"/>, never as a double. In
/// binary most one-decimal midpoints are not midpoints at all (4.35 is stored as 4.3499999999999996),
/// so on a double the rounding mode would only decide the quarters and the representation would
/// decide the rest. Every display site calls this; a guard test in RatingDisplayTests fails if a
/// rating average is rounded anywhere else.
/// </summary>
public static class RatingDisplay
{
    public static decimal? Average(int sum, int count) =>
        count > 0
            ? Math.Round((decimal)sum / count, 1, MidpointRounding.AwayFromZero)
            : null;
}
