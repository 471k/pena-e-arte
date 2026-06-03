namespace Pena_e_Arte.Domain.Exceptions;

public class StripeAccountNotConnectedException()
    : DomainException("This studio does not have a connected Stripe account. Complete onboarding first.");
