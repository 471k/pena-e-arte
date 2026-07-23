namespace Pena_e_Arte.Domain.Exceptions;

public class DuplicateNiptException()
    : DomainException("This business tax ID is already registered under a different account. " +
                       "If you're opening another location for the same business, register using " +
                       "the same owner email as your existing studio, or contact support.");
