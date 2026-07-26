namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// Whether a user has completed the first-run product tour for a given role.
/// Not tenant-scoped — a client belonging to multiple studios should not see
/// the tour again just because they're viewing a different studio.
/// </summary>
public class UserOnboardingState
{
    private UserOnboardingState() { }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid UserId { get; private set; }
    public string Role { get; private set; } = string.Empty;
    public bool HasCompletedTour { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    public static UserOnboardingState Create(Guid userId, string role) =>
        new() { UserId = userId, Role = role };

    public void MarkComplete()
    {
        HasCompletedTour = true;
        CompletedAt = DateTime.UtcNow;
    }
}
