using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.UnitTests.Helpers;

public sealed record FakeCurrentUser(Guid UserId, string Role, string? Email = null, bool IsImpersonating = false) : ICurrentUser
{
    public bool IsAuthenticated => true;

    public static FakeCurrentUser Artist() => new(Guid.NewGuid(), "artist");
    public static FakeCurrentUser Owner() => new(Guid.NewGuid(), "owner");
    public static FakeCurrentUser Client() => new(Guid.NewGuid(), "client");
    public static FakeCurrentUser Admin() => new(Guid.NewGuid(), "admin");
    public static FakeCurrentUser ImpersonatingAdmin() => new(Guid.NewGuid(), "admin", IsImpersonating: true);
}
