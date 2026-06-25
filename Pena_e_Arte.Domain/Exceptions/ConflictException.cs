namespace Pena_e_Arte.Domain.Exceptions;

public sealed class ConflictException(string message) : DomainException(message);
