namespace Pena_e_Arte.Domain.Exceptions;

public class SubscriptionRequiredException()
    : DomainException("An active subscription is required to perform this action.");
