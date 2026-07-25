namespace Pena_e_Arte.Domain.Entities;

public class Review
{
    private Review() { }

    public Guid     Id                { get; private set; } = Guid.NewGuid();
    public Guid?    StudioId          { get; private set; }
    public Guid?    ArtistId          { get; private set; }
    public Guid?    PortfolioImageId  { get; private set; }
    // Which completed appointment earned this review. Required for studio/artist
    // reviews (eligibility is per-completed-appointment, not a lifetime cap) — null
    // for portfolio-image reviews, which aren't tied to a specific booking.
    public Guid?    AppointmentId     { get; private set; }
    public Guid     AuthorUserId      { get; private set; }
    public string   AuthorName        { get; private set; } = "";
    public int      Rating            { get; private set; }
    public string   Body              { get; private set; } = "";
    public DateTime CreatedAt         { get; private set; } = DateTime.UtcNow;
    public string?  OwnerResponse     { get; private set; }
    public DateTime? OwnerResponseAt  { get; private set; }

    public static Review ForStudio(
        Guid studioId, Guid appointmentId, Guid authorUserId, string authorName, int rating, string body)
    {
        Review review = new()
        {
            StudioId      = studioId,
            AppointmentId = appointmentId,
            AuthorUserId  = authorUserId,
            AuthorName    = authorName,
            Rating        = rating,
            Body          = body.Trim(),
        };
        review.Validate();
        return review;
    }

    public static Review ForArtist(
        Guid artistId, Guid appointmentId, Guid authorUserId, string authorName, int rating, string body)
    {
        Review review = new()
        {
            ArtistId      = artistId,
            AppointmentId = appointmentId,
            AuthorUserId  = authorUserId,
            AuthorName    = authorName,
            Rating        = rating,
            Body          = body.Trim(),
        };
        review.Validate();
        return review;
    }

    public static Review ForPortfolioImage(
        Guid imageId, Guid authorUserId, string authorName, int rating, string body)
    {
        Review review = new()
        {
            PortfolioImageId = imageId,
            AuthorUserId     = authorUserId,
            AuthorName       = authorName,
            Rating           = rating,
            Body             = body.Trim(),
        };
        review.Validate();
        return review;
    }

    public void Respond(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            throw new ArgumentException("Owner response cannot be blank.", nameof(response));

        OwnerResponse   = response.Trim();
        OwnerResponseAt = DateTime.UtcNow;
    }

    private void Validate()
    {
        int targets = (StudioId.HasValue        ? 1 : 0)
                    + (ArtistId.HasValue         ? 1 : 0)
                    + (PortfolioImageId.HasValue  ? 1 : 0);

        if (targets != 1)
            throw new InvalidOperationException(
                "A Review must target exactly one of StudioId, ArtistId, or PortfolioImageId.");
    }
}
