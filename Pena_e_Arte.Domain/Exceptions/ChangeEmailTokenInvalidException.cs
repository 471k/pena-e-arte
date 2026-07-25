namespace Pena_e_Arte.Domain.Exceptions;

public class ChangeEmailTokenInvalidException()
    : DomainException("This email-change link is invalid or has expired.");
