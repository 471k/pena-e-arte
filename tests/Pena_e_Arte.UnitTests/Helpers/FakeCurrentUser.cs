using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.UnitTests.Helpers;

public sealed record FakeCurrentUser(Guid UserId, string Role, string? Email = null) : ICurrentUser
{
    public bool IsAuthenticated => true;

    public static FakeCurrentUser Artist() => new(Guid.NewGuid(), "artist");
    public static FakeCurrentUser Owner()  => new(Guid.NewGuid(), "owner");
}
