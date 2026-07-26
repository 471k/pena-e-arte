namespace Pena_e_Arte.Domain.Entities;

public class HelpSearchLog : TenantEntity
{
    public Guid UserId { get; private set; }
    public string Role { get; private set; } = string.Empty;
    public string Query { get; private set; } = string.Empty;
    public int ResultCount { get; private set; }

    private HelpSearchLog() { }

    public static HelpSearchLog Create(Guid studioId, Guid userId, string role, string query, int resultCount) =>
        new()
        {
            StudioId = studioId,
            UserId = userId,
            Role = role,
            Query = query.Trim().ToLowerInvariant(),
            ResultCount = resultCount,
        };
}
