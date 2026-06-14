namespace Pena_e_Arte.Domain.Interfaces;

public interface ICurrentUser
{
    Guid    UserId          { get; }
    string  Role            { get; }
    string? Email           { get; }
    bool    IsAuthenticated { get; }
}
