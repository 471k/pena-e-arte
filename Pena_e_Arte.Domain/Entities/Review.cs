namespace Pena_e_Arte.Domain.Entities;

public class Review
{
    private Review() { }

    public Guid     Id           { get; private set; } = Guid.NewGuid();
    public Guid?    StudioId     { get; private set; }
    public Guid?    ArtistId     { get; private set; }
    public Guid     AuthorUserId { get; private set; }
    public string   AuthorName   { get; private set; } = "";
    public int      Rating       { get; private set; }
    public string   Body         { get; private set; } = "";
    public DateTime CreatedAt    { get; private set; } = DateTime.UtcNow;

    public static Review ForStudio(
        Guid studioId, Guid authorUserId, string authorName, int rating, string body)
        => new()
        {
            StudioId     = studioId,
            AuthorUserId = authorUserId,
            AuthorName   = authorName,
            Rating       = rating,
            Body         = body.Trim(),
        };

    public static Review ForArtist(
        Guid artistId, Guid authorUserId, string authorName, int rating, string body)
        => new()
        {
            ArtistId     = artistId,
            AuthorUserId = authorUserId,
            AuthorName   = authorName,
            Rating       = rating,
            Body         = body.Trim(),
        };
}
