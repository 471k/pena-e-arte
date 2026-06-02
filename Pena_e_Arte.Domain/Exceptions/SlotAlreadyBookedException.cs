namespace Pena_e_Arte.Domain.Exceptions;

public class SlotAlreadyBookedException()
    : DomainException("The selected time slot is no longer available.");
