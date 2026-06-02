namespace Pena_e_Arte.Domain.Exceptions;

public class DesignAlreadyApprovedException()
    : DomainException("This design revision has already been approved.");
