namespace Pena_e_Arte.Domain.Exceptions;

/// <summary>
/// The cancellation quote the owner confirmed no longer matches the one the server computes now (for example a
/// month boundary passed while the dialog was open). Nothing was cancelled and nothing was refunded; the owner
/// reviews the new quote and confirms again. Mapped to 409 with code CANCELLATION_QUOTE_CHANGED.
/// </summary>
public sealed class CancellationQuoteChangedException()
    : DomainException("Your refund changed since you opened this. Nothing was cancelled. Check the new amount and confirm again.");
