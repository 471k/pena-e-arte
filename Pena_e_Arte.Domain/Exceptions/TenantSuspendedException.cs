namespace Pena_e_Arte.Domain.Exceptions;

public class TenantSuspendedException()
    : DomainException("This studio account has been suspended. Please contact support.");
