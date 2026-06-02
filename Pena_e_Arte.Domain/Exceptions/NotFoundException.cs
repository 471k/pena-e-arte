namespace Pena_e_Arte.Domain.Exceptions;

public class NotFoundException(string entityName, object key)
    : DomainException($"{entityName} '{key}' was not found.");
