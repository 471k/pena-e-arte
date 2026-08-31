namespace Pena_e_Arte.Domain.Exceptions;

public class AccountAlreadyExistsException()
    : DomainException("An account already exists with this email. Please log in to book with this address.");
