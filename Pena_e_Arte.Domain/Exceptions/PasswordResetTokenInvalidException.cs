namespace Pena_e_Arte.Domain.Exceptions;

public class PasswordResetTokenInvalidException()
    : DomainException("This reset link is invalid or has expired.");
