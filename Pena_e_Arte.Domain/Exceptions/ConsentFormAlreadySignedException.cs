namespace Pena_e_Arte.Domain.Exceptions;

public class ConsentFormAlreadySignedException()
    : DomainException("A consent form has already been signed for this appointment.");
