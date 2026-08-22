namespace Pena_e_Arte.Domain.Exceptions;

public class ManualReminderQuotaExceededException()
    : DomainException("You've reached today's limit for manual reminders. Try again tomorrow, " +
                       "or contact support if you need a higher limit.");
