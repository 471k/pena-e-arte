namespace Pena_e_Arte.Domain.Exceptions;

public class PlanLimitExceededException(string message) : DomainException(message);
