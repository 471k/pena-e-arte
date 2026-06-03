namespace Pena_e_Arte.Domain.Exceptions;

public class SubscriptionRequiredException(
    string message = "An active subscription is required to perform this action.")
    : DomainException(message);
